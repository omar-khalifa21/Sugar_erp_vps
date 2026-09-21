using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SugarERP.Application;
using SugarERP.Domain;
using SugarERP.Infrastructure.Local;

namespace SugarERP.Sync.Client;

public sealed class BranchSyncService(HttpClient httpClient, LocalDatabase database) : IBranchSyncService
{
    private const string ContractVersion = "1.0";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _syncGate = new(1, 1);

    public async Task<ConnectivityResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        await using var db = database.CreateContext();
        var configuration = await db.DeviceConfigurations.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (configuration is null)
            return new ConnectivityResult(false, false, "سجّل الجهاز أولاً لاختبار عنوان الخادم.");
        if (!Uri.TryCreate(configuration.ApiBaseUrl.Trim().TrimEnd('/'), UriKind.Absolute, out var apiBase))
            return new ConnectivityResult(false, false, "عنوان الخادم المحفوظ غير صالح.");
        var client = new CentralApiClient(httpClient);
        try
        {
            var health = await client.GetHealthAsync(apiBase, cancellationToken);
            var ready = await client.GetReadyAsync(apiBase, cancellationToken);
            var message = health.Reachable && ready.Reachable
                ? "الخادم يعمل وقاعدة البيانات جاهزة."
                : health.Reachable
                    ? "الخادم يعمل لكن قاعدة البيانات أو الترحيلات ليست جاهزة."
                    : "لا يمكن الوصول إلى الخادم الآن.";
            return new ConnectivityResult(health.Reachable, ready.Reachable, message);
        }
        catch (ArgumentException)
        {
            return new ConnectivityResult(false, false, "استخدم HTTPS ينتهي بـ /api/v1، أو localhost للتطوير.");
        }
    }

    public async Task<SyncRunResult> SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        await _syncGate.WaitAsync(cancellationToken);
        try { return await SynchronizeCoreAsync(cancellationToken); }
        finally { _syncGate.Release(); }
    }

    private async Task<SyncRunResult> SynchronizeCoreAsync(CancellationToken cancellationToken)
    {
        var pushed = await PushPendingAsync(cancellationToken);
        DeviceConfiguration? configuration;
        string? cursor;
        await using (var db = database.CreateContext())
        {
            configuration = await db.DeviceConfigurations.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            cursor = await db.SyncCursors.AsNoTracking()
                .Where(value => value.FeedScope == "device")
                .Select(value => value.Cursor)
                .SingleOrDefaultAsync(cancellationToken);
        }
        if (configuration is null) return pushed;

        try
        {
            var secret = DeviceCredentialProtector.Unprotect(configuration.DeviceCredential);
            if (!Uri.TryCreate(configuration.ApiBaseUrl.Trim().TrimEnd('/'), UriKind.Absolute, out var apiBase))
                throw new CentralApiException("INVALID_API_URL", "عنوان الخادم غير صالح.", false, 0);
            var connection = new DeviceConnection(apiBase, configuration.DeviceId, secret);
            var client = new CentralApiClient(httpClient);
            var bootstrap = await client.GetBootstrapAsync(connection, cancellationToken);
            var received = await IncomingSyncApplier.ApplyCatalogSnapshotAsync(database, configuration, bootstrap, cancellationToken);
            var pageCount = 0;
            bool hasMore;
            do
            {
                var page = await client.PullAsync(connection, cursor, 100, cancellationToken);
                received += await IncomingSyncApplier.ApplyPageAsync(database, configuration, page, cancellationToken);
                var acknowledgement = await client.AcknowledgeAsync(connection, page.Cursor, cancellationToken);
                if (!acknowledgement.Acknowledged)
                    throw new CentralApiException("ACK_REJECTED", "لم يؤكد الخادم مؤشر المزامنة.", true, 502);
                cursor = page.Cursor;
                hasMore = page.HasMore;
                pageCount += 1;
            } while (hasMore && pageCount < 25);

            var reportUploads = await UploadPendingReportsAsync(connection, cancellationToken);
            await using var finalDb = database.CreateContext();
            var remaining = await RemainingAsync(finalDb, cancellationToken) + reportUploads.Remaining;
            var message = hasMore
                ? $"تم تنزيل {received} تحديث. توجد صفحات أخرى وستستكمل في المزامنة التالية."
                : pushed.Succeeded && reportUploads.Succeeded
                    ? $"اكتملت المزامنة: رُفع {pushed.Acknowledged} ونزل {received} تحديث، ورُفع {reportUploads.Uploaded} تقرير."
                    : !reportUploads.Succeeded
                        ? $"اكتملت مزامنة الحركات، لكن بقي تقرير وردية للرفع: {reportUploads.Message}"
                    : $"نزل {received} تحديث. ما زالت بعض الحركات المحلية بانتظار الرفع: {pushed.UserMessage}";
            return new SyncRunResult(pushed.Sent, pushed.Acknowledged, remaining, message, pushed.Succeeded && reportUploads.Succeeded && !hasMore, received);
        }
        catch (CentralApiException exception)
        {
            return pushed with
            {
                UserMessage = exception.Code switch
                {
                    "STREAM_EPOCH_MISMATCH" => "نسخة الجهاز قديمة وتحتاج إعادة اعتماد من الإدارة. الحركات المحلية محفوظة.",
                    "CONTENT_HASH_MISMATCH" or "IDEMPOTENCY_KEY_REUSE" => "توقفت المزامنة لحماية سلامة البيانات. لم تُحذف أي حركة.",
                    "DEPENDENCY_NOT_READY" => exception.SafeMessage,
                    _ when exception.Retryable => "تعذر تنزيل تحديثات الخادم الآن. ستتم إعادة المحاولة دون فقد البيانات.",
                    _ => exception.SafeMessage
                },
                Succeeded = false
            };
        }
        catch (HttpRequestException)
        {
            return pushed with { UserMessage = "تعذر تنزيل تحديثات الخادم. البيانات المحلية محفوظة.", Succeeded = false };
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return pushed with { UserMessage = "انتهت مهلة تنزيل التحديثات. ستتم المحاولة لاحقاً.", Succeeded = false };
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or PlatformNotSupportedException or JsonException)
        {
            return pushed with { UserMessage = "تعذر التحقق من إعدادات أو رد المزامنة. لم يتم تطبيق بيانات ناقصة.", Succeeded = false };
        }
    }

    private async Task<ReportUploadRun> UploadPendingReportsAsync(
        DeviceConnection connection,
        CancellationToken cancellationToken)
    {
        var uploaded = 0;
        for (var index = 0; index < 10; index++)
        {
            await using var db = database.CreateContext();
            var now = DateTimeOffset.UtcNow;
            var job = await db.SideEffectJobs
                .Where(value => value.Kind == SideEffectKind.UploadShiftReport
                    && value.State != SideEffectState.Completed
                    && value.State != SideEffectState.Failed
                    && value.NextAttemptAtUtc <= now)
                .OrderBy(value => value.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (job is null) break;
            var artifact = await db.ReportArtifacts.SingleOrDefaultAsync(
                value => value.ShiftId == job.SourceId && value.ReportVersion == job.DocumentVersion,
                cancellationToken);
            if (artifact is null) break;
            var shift = await db.Shifts.AsNoTracking().SingleAsync(value => value.Id == job.SourceId, cancellationToken);
            job.State = SideEffectState.Running;
            job.Attempts += 1;
            await db.SaveChangesAsync(cancellationToken);
            try
            {
                if (!File.Exists(artifact.LocalPath)) throw new InvalidDataException("REPORT_FILE_MISSING");
                var bytes = await File.ReadAllBytesAsync(artifact.LocalPath, cancellationToken);
                var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                if (bytes.LongLength != artifact.ByteLength || !string.Equals(hash, artifact.ContentHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("REPORT_FILE_CHANGED");
                var response = await new CentralApiClient(httpClient).UploadShiftReportAsync(
                    connection,
                    shift.Id,
                    artifact.ReportVersion,
                    shift.BusinessDate,
                    shift.Kind == ShiftKind.Morning ? "MORNING" : "EVENING",
                    Path.GetFileName(artifact.LocalPath),
                    artifact.ContentHash,
                    bytes,
                    cancellationToken);
                job.State = SideEffectState.Completed;
                job.CompletedAtUtc = response.UploadedAt;
                job.LastError = null;
                artifact.UploadedAtUtc = response.UploadedAt;
                artifact.Error = null;
                uploaded += 1;
            }
            catch (CentralApiException exception)
            {
                job.LastError = exception.Code;
                artifact.Error = exception.Code;
                if (exception.Retryable || exception.StatusCode == 404)
                {
                    job.State = SideEffectState.Pending;
                    job.NextAttemptAtUtc = now.AddSeconds(ReportRetrySeconds(job.Attempts));
                }
                else
                {
                    job.State = SideEffectState.Failed;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                job.State = SideEffectState.Pending;
                job.LastError = exception is TaskCanceledException ? "REPORT_UPLOAD_TIMEOUT" : "REPORT_UPLOAD_OFFLINE";
                artifact.Error = job.LastError;
                job.NextAttemptAtUtc = now.AddSeconds(ReportRetrySeconds(job.Attempts));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                job.State = SideEffectState.Failed;
                job.LastError = exception.Message;
                artifact.Error = exception.Message;
            }
            await db.SaveChangesAsync(cancellationToken);
            if (job.State != SideEffectState.Completed) break;
        }

        await using var resultDb = database.CreateContext();
        var remaining = await resultDb.SideEffectJobs.CountAsync(
            value => value.Kind == SideEffectKind.UploadShiftReport && value.State != SideEffectState.Completed,
            cancellationToken);
        return new ReportUploadRun(uploaded, remaining, remaining == 0,
            remaining == 0 ? "تم رفع كل التقارير." : "سيعاد رفع التقرير تلقائياً بعد الحفاظ على نسخته المحلية.");
    }

    private static double ReportRetrySeconds(int attempts) =>
        Math.Min(300, Math.Pow(2, Math.Min(attempts, 8)));

    public async Task<SyncRunResult> PushPendingAsync(CancellationToken cancellationToken = default)
    {
        await using var db = database.CreateContext();
        var configuration = await db.DeviceConfigurations.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (configuration is null) return new SyncRunResult(0, 0, 0, "سجّل الجهاز أولاً قبل المزامنة.", false);

        var now = DateTimeOffset.UtcNow;
        // Versions before the Kitchen cafe-order fix permanently rejected these event types
        // as WRONG_PROFILE. Re-open only that known server-side misclassification so real,
        // already-created records can reach the server after an upgrade.
        var recoverableWrongProfileEvents = await db.OutboxMessages
            .Where(value => value.State == OutboxState.Failed
                && value.LastErrorCode == "WRONG_PROFILE"
                && (value.EventType == "custom_order.created"
                    || value.EventType == "custom_order.status_changed"
                    || value.EventType == "custom_customer.payment_recorded"
                    || value.EventType == "cafe_customer.created"
                    || value.EventType == "cafe_customer.price_list_updated"
                    || value.EventType == "cafe_customer.archived"))
            .ToListAsync(cancellationToken);
        if (recoverableWrongProfileEvents.Count > 0)
        {
            foreach (var message in recoverableWrongProfileEvents)
            {
                message.State = OutboxState.Pending;
                message.NextAttemptAtUtc = now;
                message.LastErrorCode = null;
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        var outstanding = await db.OutboxMessages
            .Where(value => value.State != OutboxState.Acknowledged)
            .OrderBy(value => value.DeviceSequence)
            .Take(100)
            .ToListAsync(cancellationToken);
        if (outstanding.Count == 0) return new SyncRunResult(0, 0, 0, "كل الحركات مرفوعة بالفعل.", true);

        var firstOutstanding = outstanding[0];
        if (firstOutstanding.State == OutboxState.Failed)
        {
            return new SyncRunResult(
                0,
                0,
                await RemainingAsync(db, cancellationToken),
                FriendlyPermanentSyncError(firstOutstanding.LastErrorCode ?? "SYNC_REJECTED"),
                false);
        }
        if (firstOutstanding.NextAttemptAtUtc > now)
        {
            return new SyncRunResult(
                0,
                0,
                await RemainingAsync(db, cancellationToken),
                "تعذرت المحاولة السابقة. ستتم إعادة المزامنة تلقائياً بعد مهلة قصيرة.",
                false);
        }

        // The server accepts an atomic, ordered prefix. Never skip a delayed or permanently
        // failed event and send a later device sequence ahead of it.
        var pending = outstanding
            .TakeWhile(value => value.State != OutboxState.Failed && value.NextAttemptAtUtc <= now)
            .ToList();
        if (pending.Count == 0)
        {
            return new SyncRunResult(0, 0, await RemainingAsync(db, cancellationToken), "لا توجد حركات جاهزة للإرسال الآن.", false);
        }

        if (!TryBuildPushEndpoint(configuration.ApiBaseUrl, out var endpoint))
        {
            return new SyncRunResult(
                0,
                0,
                await RemainingAsync(db, cancellationToken),
                "عنوان الخادم غير صالح. استخدم عنوان HTTPS ينتهي بـ /api/v1، أو localhost للتطوير المحلي.",
                false);
        }
        if (configuration.DeviceId == Guid.Empty || string.IsNullOrWhiteSpace(configuration.DeviceCredential))
        {
            return new SyncRunResult(
                0,
                0,
                await RemainingAsync(db, cancellationToken),
                "بيانات تسجيل الجهاز غير مكتملة. أعد التسجيل قبل المزامنة.",
                false);
        }

        string deviceCredential;
        try
        {
            deviceCredential = DeviceCredentialProtector.Unprotect(configuration.DeviceCredential);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or PlatformNotSupportedException)
        {
            return new SyncRunResult(
                0,
                0,
                await RemainingAsync(db, cancellationToken),
                "تعذر فتح بيانات اعتماد الجهاز لهذا المستخدم. أعد تسجيل الجهاز؛ الحركات المحلية محفوظة.",
                false);
        }

        var events = pending.Select(message => new
        {
            id = message.EventId,
            device_sequence = message.DeviceSequence,
            event_type = message.EventType,
            schema_version = message.SchemaVersion,
            occurred_at = message.OccurredAtUtc.ToUniversalTime().ToString("O"),
            payload = JsonSerializer.Deserialize<JsonElement>(message.PayloadJson),
            dependencies = JsonSerializer.Deserialize<string[]>(message.DependenciesJson) ?? [],
            content_hash = message.ContentHash
        }).ToArray();
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new { contract_version = ContractVersion, stream_epoch = configuration.StreamEpoch, events }, options: JsonOptions)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-Device-Id", configuration.DeviceId.ToString());
        request.Headers.Add("X-Device-Secret", deviceCredential);
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await ReadErrorAsync(response, cancellationToken);
                var code = error?.Code ?? $"HTTP_{(int)response.StatusCode}";
                var retryable = error?.Retryable ?? IsTransient(response.StatusCode);
                if (retryable) MarkRetry(pending, code, now);
                else MarkPermanentFailure(pending, code, now);
                await db.SaveChangesAsync(cancellationToken);
                return new SyncRunResult(
                    pending.Count,
                    0,
                    await RemainingAsync(db, cancellationToken),
                    retryable ? FriendlyRetryableSyncError(code) : FriendlyPermanentSyncError(code),
                    false);
            }
            var result = await response.Content.ReadFromJsonAsync<PushResponse>(JsonOptions, cancellationToken)
                ?? throw new JsonException("Missing sync response");
            ValidateResponseEnvelope(result);

            var pendingById = pending.ToDictionary(value => value.EventId);
            var resultsById = new Dictionary<Guid, PushResult>();
            var duplicateResultIds = new HashSet<Guid>();
            var responseWasInvalid = false;
            foreach (var item in result.Results!)
            {
                if (!pendingById.ContainsKey(item.Id))
                {
                    // A result for an event that was not in this request can never
                    // acknowledge one of our local events.
                    responseWasInvalid = true;
                    continue;
                }

                if (!resultsById.TryAdd(item.Id, item))
                {
                    duplicateResultIds.Add(item.Id);
                    responseWasInvalid = true;
                }
            }

            var acknowledged = 0;
            string? firstRetryableCode = null;
            string? firstPermanentCode = null;
            foreach (var message in pending)
            {
                if (!resultsById.TryGetValue(message.EventId, out var item))
                {
                    MarkRetry([message], "INCOMPLETE_SYNC_RESPONSE", now);
                    firstRetryableCode ??= "INCOMPLETE_SYNC_RESPONSE";
                    responseWasInvalid = true;
                    continue;
                }

                if (duplicateResultIds.Contains(message.EventId))
                {
                    MarkRetry([message], "DUPLICATE_SYNC_RESULT", now);
                    firstRetryableCode ??= "DUPLICATE_SYNC_RESULT";
                    continue;
                }

                var status = (item.Status ?? string.Empty).Trim().ToLowerInvariant();
                var code = FirstNonEmpty(item.Code, item.ErrorCode);
                switch (status)
                {
                    case "accepted":
                    case "duplicate":
                        if (string.IsNullOrWhiteSpace(item.ServerPosition))
                        {
                            MarkRetry([message], "INVALID_SYNC_RESPONSE", now);
                            firstRetryableCode ??= "INVALID_SYNC_RESPONSE";
                            responseWasInvalid = true;
                            break;
                        }

                        message.State = OutboxState.Acknowledged;
                        message.AcknowledgedAtUtc = now;
                        message.LastErrorCode = null;
                        acknowledged += 1;
                        break;

                    case "retryable":
                        code ??= "SYNC_RETRYABLE";
                        MarkRetry([message], code, now);
                        firstRetryableCode ??= code;
                        break;

                    case "rejected":
                        code ??= "SYNC_REJECTED";
                        MarkPermanentFailure([message], code, now);
                        firstPermanentCode ??= code;
                        break;

                    case "failed" when item.Retryable is true:
                        code ??= "SYNC_RETRYABLE";
                        MarkRetry([message], code, now);
                        firstRetryableCode ??= code;
                        break;

                    case "failed" when item.Retryable is false:
                        code ??= "SYNC_REJECTED";
                        MarkPermanentFailure([message], code, now);
                        firstPermanentCode ??= code;
                        break;

                    default:
                        // An unknown or unclassified status is not durable proof of
                        // acceptance or permanent rejection. Preserve it for retry.
                        MarkRetry([message], "UNRECOGNIZED_SYNC_RESULT", now);
                        firstRetryableCode ??= "UNRECOGNIZED_SYNC_RESULT";
                        responseWasInvalid = true;
                        break;
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            var remaining = await RemainingAsync(db, cancellationToken);
            if (firstPermanentCode is not null)
            {
                return new SyncRunResult(
                    pending.Count,
                    acknowledged,
                    remaining,
                    FriendlyPermanentSyncError(firstPermanentCode),
                    false);
            }
            if (firstRetryableCode is not null || responseWasInvalid)
            {
                return new SyncRunResult(
                    pending.Count,
                    acknowledged,
                    remaining,
                    firstRetryableCode is null
                        ? "احتوى رد المزامنة على نتائج غير مرتبطة بهذه الدفعة. تم تأكيد الحركات الموثقة فقط."
                        : FriendlyRetryableSyncError(firstRetryableCode),
                    false);
            }
            return new SyncRunResult(pending.Count, acknowledged, remaining, "تمت المزامنة بنجاح.", true);
        }
        catch (HttpRequestException)
        {
            MarkRetry(pending, "OFFLINE", now);
            await db.SaveChangesAsync(cancellationToken);
            return new SyncRunResult(pending.Count, 0, await RemainingAsync(db, cancellationToken), "لا يوجد اتصال بالخادم. بياناتك محفوظة وستُرفع لاحقاً.", false);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            MarkRetry(pending, "TIMEOUT", now);
            await db.SaveChangesAsync(cancellationToken);
            return new SyncRunResult(pending.Count, 0, await RemainingAsync(db, cancellationToken), "الخادم بطيء الآن. بياناتك محفوظة وستتم المحاولة لاحقاً.", false);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            MarkRetry(pending, "INVALID_RESPONSE", now);
            await db.SaveChangesAsync(cancellationToken);
            return new SyncRunResult(pending.Count, 0, await RemainingAsync(db, cancellationToken), "وصل رد غير مكتمل من الخادم. بياناتك محفوظة وستتم المحاولة لاحقاً.", false);
        }
    }

    private static bool TryBuildPushEndpoint(string apiBaseUrl, out Uri endpoint)
    {
        endpoint = null!;
        if (!Uri.TryCreate(apiBaseUrl.Trim().TrimEnd('/') + '/', UriKind.Absolute, out var baseUri)) return false;
        var isSecure = baseUri.Scheme == Uri.UriSchemeHttps;
        var isLocalDevelopment = baseUri.Scheme == Uri.UriSchemeHttp && baseUri.IsLoopback;
        if (!isSecure && !isLocalDevelopment) return false;
        if (!baseUri.AbsolutePath.TrimEnd('/').EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase)) return false;
        endpoint = new Uri(baseUri, "sync/push");
        return true;
    }

    private static void MarkRetry(IEnumerable<OutboxMessage> messages, string code, DateTimeOffset now)
    {
        foreach (var message in messages)
        {
            message.State = OutboxState.Pending;
            message.Attempts += 1;
            message.LastErrorCode = code;
            var delaySeconds = Math.Min(300, Math.Pow(2, Math.Min(message.Attempts, 8)));
            var jitteredSeconds = Math.Min(300, delaySeconds * (0.75 + Random.Shared.NextDouble() * 0.5));
            message.NextAttemptAtUtc = now.AddSeconds(jitteredSeconds);
        }
    }

    private static void MarkPermanentFailure(IEnumerable<OutboxMessage> messages, string code, DateTimeOffset now)
    {
        foreach (var message in messages)
        {
            message.State = OutboxState.Failed;
            message.Attempts += 1;
            message.LastErrorCode = code;
            message.NextAttemptAtUtc = now;
        }
    }

    private static Task<int> RemainingAsync(BranchDbContext db, CancellationToken cancellationToken) =>
        db.OutboxMessages.CountAsync(value => value.State != OutboxState.Acknowledged, cancellationToken);

    private static async Task<ApiError?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try { return await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions, cancellationToken); }
        catch (JsonException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private static string FriendlyRetryableSyncError(string code) => code switch
    {
        "SEQUENCE_GAP" => "الخادم ينتظر حركة أقدم. ستُعاد المحاولة بالترتيب.",
        "DEPENDENCY_NOT_READY" => "إحدى الحركات تعتمد على حركة لم تصل بعد. ستُعاد المحاولة بالترتيب.",
        "RATE_LIMITED" => "الخادم مشغول الآن. ستُعاد المحاولة تلقائياً بعد مهلة قصيرة.",
        "UNRECOGNIZED_SYNC_RESULT" => "أعاد الخادم حالة غير معروفة لإحدى الحركات. لم يتم تأكيدها وستُعاد المحاولة بأمان.",
        "INCOMPLETE_SYNC_RESPONSE" or "DUPLICATE_SYNC_RESULT" or "INVALID_SYNC_RESPONSE" =>
            "وصل رد مزامنة غير مكتمل. تم تأكيد الحركات الموثقة فقط وستُعاد محاولة الباقي بأمان.",
        _ => "تعذر رفع الحركات الآن. البيانات محفوظة ولم يتم حذف أي شيء."
    };

    private static string FriendlyPermanentSyncError(string code) => code switch
    {
        "STREAM_EPOCH_MISMATCH" => "نسخة الجهاز قديمة وتحتاج مراجعة من الإدارة قبل استكمال المزامنة. لم تُحذف أي حركة.",
        "UNAUTHENTICATED" => "تم إلغاء تسجيل هذا الجهاز أو انتهت بيانات اعتماده. تواصل مع الإدارة؛ الحركات المحلية محفوظة.",
        "CONTENT_HASH_MISMATCH" => "تعذر التحقق من سلامة حركة محلية. أُوقفت المزامنة لحمايتها وتحتاج مراجعة.",
        "IDEMPOTENCY_KEY_REUSE" => "اكتشف الخادم تعارضاً في هوية حركة. أُوقفت المزامنة وتحتاج مراجعة فنية.",
        "SEQUENCE_REPLAY" => "تعارض ترتيب هذا الجهاز مع الخادم. أُوقفت المزامنة دون حذف الحركات.",
        _ => $"رفض الخادم الحركات نهائياً ({code}). لم تُحذف، وتحتاج مراجعة قبل متابعة المزامنة."
    };

    private static string? FirstNonEmpty(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) ? first : !string.IsNullOrWhiteSpace(second) ? second : null;

    private static void ValidateResponseEnvelope(PushResponse response)
    {
        if (!string.Equals(response.ContractVersion, ContractVersion, StringComparison.Ordinal)
            || response.NextExpectedSequence < 1
            || response.Results is null)
            throw new JsonException("Invalid sync response envelope");
    }

    private sealed class PushResponse
    {
        [JsonPropertyName("contract_version")]
        public string ContractVersion { get; init; } = string.Empty;

        [JsonPropertyName("next_expected_sequence")]
        public int NextExpectedSequence { get; init; }

        [JsonPropertyName("results")]
        public PushResult[]? Results { get; init; } = [];
    }

    private sealed record ReportUploadRun(int Uploaded, int Remaining, bool Succeeded, string Message);

    private sealed class PushResult
    {
        [JsonPropertyName("id")]
        public Guid Id { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; } = string.Empty;

        [JsonPropertyName("server_position")]
        public string ServerPosition { get; init; } = string.Empty;

        [JsonPropertyName("code")]
        public string? Code { get; init; }

        [JsonPropertyName("error_code")]
        public string? ErrorCode { get; init; }

        [JsonPropertyName("retryable")]
        public bool? Retryable { get; init; }
    }

    private sealed class ApiError
    {
        [JsonPropertyName("code")]
        public string Code { get; init; } = "SYNC_REJECTED";

        [JsonPropertyName("retryable")]
        public bool? Retryable { get; init; }
    }
}
