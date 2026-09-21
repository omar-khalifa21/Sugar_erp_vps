using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SugarERP.Application;

namespace SugarERP.Sync.Client;

public sealed record CentralEndpointStatus(bool Reachable, int? HttpStatus, string Endpoint, string Message);
public sealed record DeviceConnection(Uri ApiBaseUrl, Guid DeviceId, string DeviceSecret, string? AppVersion = null);

public sealed record SyncUploadEvent(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("device_sequence")] int DeviceSequence,
    [property: JsonPropertyName("event_type")] string EventType,
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("occurred_at")] string OccurredAt,
    [property: JsonPropertyName("payload")] JsonElement Payload,
    [property: JsonPropertyName("dependencies")] string[] Dependencies,
    [property: JsonPropertyName("content_hash")] string ContentHash);

public sealed record SyncPushResult(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("server_position")] string? ServerPosition,
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("error_code")] string? ErrorCode,
    [property: JsonPropertyName("retryable")] bool? Retryable);

public sealed record SyncPushResponse(
    [property: JsonPropertyName("contract_version")] string ContractVersion,
    [property: JsonPropertyName("next_expected_sequence")] int NextExpectedSequence,
    [property: JsonPropertyName("results")] SyncPushResult[] Results);

public sealed record SyncPulledEvent(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("origin_device_id")] Guid OriginDeviceId,
    [property: JsonPropertyName("origin_site_id")] Guid OriginSiteId,
    [property: JsonPropertyName("stream_epoch")] int StreamEpoch,
    [property: JsonPropertyName("device_sequence")] int DeviceSequence,
    [property: JsonPropertyName("event_type")] string EventType,
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("occurred_at")] string OccurredAt,
    [property: JsonPropertyName("received_at")] string ReceivedAt,
    [property: JsonPropertyName("payload")] JsonElement Payload,
    [property: JsonPropertyName("dependencies")] Guid[] Dependencies,
    [property: JsonPropertyName("content_hash")] string ContentHash,
    [property: JsonPropertyName("server_position")] string ServerPosition);

public sealed record SyncCompatibility(
    [property: JsonPropertyName("minimum")] string Minimum,
    [property: JsonPropertyName("current")] string Current);

public sealed record SyncPullResponse(
    [property: JsonPropertyName("contract_version")] string ContractVersion,
    [property: JsonPropertyName("compatibility")] SyncCompatibility Compatibility,
    [property: JsonPropertyName("cursor")] string Cursor,
    [property: JsonPropertyName("has_more")] bool HasMore,
    [property: JsonPropertyName("events")] SyncPulledEvent[] Events);

public sealed record SyncAckResponse(
    [property: JsonPropertyName("acknowledged")] bool Acknowledged,
    [property: JsonPropertyName("server_position")] string ServerPosition);

public sealed record ReportUploadResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("uploaded_at")] DateTimeOffset UploadedAt);

public sealed class CentralApiException : Exception
{
    public CentralApiException(
        string code,
        string safeMessage,
        bool retryable,
        int statusCode,
        string? correlationId = null,
        Exception? innerException = null) : base(safeMessage, innerException)
    {
        Code = code;
        SafeMessage = safeMessage;
        Retryable = retryable;
        StatusCode = statusCode;
        CorrelationId = correlationId;
    }

    public string Code { get; }
    public string SafeMessage { get; }
    public bool Retryable { get; }
    public int StatusCode { get; }
    public string? CorrelationId { get; }
}

public sealed class CentralApiClient(HttpClient httpClient)
{
    private const string ContractVersion = "1.0";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<CentralEndpointStatus> GetHealthAsync(Uri apiBaseUrl, CancellationToken cancellationToken = default) =>
        ProbeAsync(apiBaseUrl, "health", cancellationToken);

    public Task<CentralEndpointStatus> GetReadyAsync(Uri apiBaseUrl, CancellationToken cancellationToken = default) =>
        ProbeAsync(apiBaseUrl, "ready", cancellationToken);

    public async Task<EnrollmentResult> EnrollAsync(EnrollmentCommand command, CancellationToken cancellationToken = default)
    {
        var endpoint = BuildEndpoint(command.ApiBaseUrl, "enrollment");
        using var response = await httpClient.PostAsJsonAsync(endpoint, new
        {
            token = command.EnrollmentToken.Trim(),
            deviceName = command.DeviceName.Trim(),
            keyThumbprint = command.KeyThumbprint.ToLowerInvariant(),
            appVersion = command.AppVersion,
            expectedProfile = command.ExpectedProfile switch {
                SugarERP.Domain.DeviceProfile.BranchType1 => "BRANCH_TYPE_1",
                SugarERP.Domain.DeviceProfile.BranchType2 => "BRANCH_TYPE_2",
                SugarERP.Domain.DeviceProfile.Kitchen => "KITCHEN",
                _ => null
            }
        }, JsonOptions, cancellationToken);
        var body = await ReadSuccessAsync<EnrollmentWireResponse>(response, cancellationToken);
        if (!string.Equals(body.ContractVersion, ContractVersion, StringComparison.Ordinal)
            || body.Device.Id == Guid.Empty
            || body.Device.SiteId == Guid.Empty
            || body.Device.StreamEpoch < 1
            || string.IsNullOrWhiteSpace(body.Credential))
            throw InvalidResponse(response);
        var profile = body.Device.Profile switch
        {
            "BRANCH_TYPE_1" => SugarERP.Domain.DeviceProfile.BranchType1,
            "BRANCH_TYPE_2" => SugarERP.Domain.DeviceProfile.BranchType2,
            "KITCHEN" => SugarERP.Domain.DeviceProfile.Kitchen,
            _ => throw new CentralApiException("INVALID_PROFILE", "نوع جهاز غير معروف في رد الخادم.", false, (int)response.StatusCode)
        };
        return new EnrollmentResult(body.Device.SiteId, body.Device.Id, profile, body.Credential, body.Device.StreamEpoch);
    }

    public async Task<JsonElement> GetBootstrapAsync(DeviceConnection connection, CancellationToken cancellationToken = default)
    {
        using var request = CreateDeviceRequest(connection, HttpMethod.Get, "sync/bootstrap");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await ReadSuccessAsync<JsonElement>(response, cancellationToken);
        if (body.ValueKind != JsonValueKind.Object
            || !body.TryGetProperty("contract_version", out var version)
            || version.GetString() != ContractVersion
            || !body.TryGetProperty("catalog", out var catalog)
            || catalog.ValueKind != JsonValueKind.Array)
            throw InvalidResponse(response);
        return body.Clone();
    }

    public async Task<SyncPushResponse> PushAsync(
        DeviceConnection connection,
        int streamEpoch,
        IReadOnlyList<SyncUploadEvent> events,
        CancellationToken cancellationToken = default)
    {
        if (streamEpoch < 1 || events.Count > 100) throw new ArgumentOutOfRangeException(nameof(streamEpoch));
        using var request = CreateDeviceRequest(connection, HttpMethod.Post, "sync/push");
        request.Content = JsonContent.Create(new
        {
            contract_version = ContractVersion,
            stream_epoch = streamEpoch,
            events
        }, options: JsonOptions);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await ReadSuccessAsync<SyncPushResponse>(response, cancellationToken);
        if (!string.Equals(body.ContractVersion, ContractVersion, StringComparison.Ordinal)
            || body.NextExpectedSequence < 1
            || body.Results is null)
            throw InvalidResponse(response);
        return body;
    }

    public async Task<SyncPullResponse> PullAsync(
        DeviceConnection connection,
        string? cursor,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        var query = string.IsNullOrWhiteSpace(cursor)
            ? $"sync/pull?limit={limit}"
            : $"sync/pull?cursor={Uri.EscapeDataString(cursor)}&limit={limit}";
        using var request = CreateDeviceRequest(connection, HttpMethod.Get, query);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await ReadSuccessAsync<SyncPullResponse>(response, cancellationToken);
        if (!string.Equals(body.ContractVersion, ContractVersion, StringComparison.Ordinal)
            || body.Compatibility is null
            || !string.Equals(body.Compatibility.Minimum, ContractVersion, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(body.Cursor)
            || body.Events is null
            || body.Events.Length > 100)
            throw InvalidResponse(response);
        return body;
    }

    public async Task<SyncAckResponse> AcknowledgeAsync(
        DeviceConnection connection,
        string cursor,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cursor)) throw new ArgumentException("Cursor is required.", nameof(cursor));
        using var request = CreateDeviceRequest(connection, HttpMethod.Post, "sync/ack");
        request.Content = JsonContent.Create(new { cursor }, options: JsonOptions);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadSuccessAsync<SyncAckResponse>(response, cancellationToken);
    }

    public async Task<ReportUploadResponse> UploadShiftReportAsync(
        DeviceConnection connection,
        Guid shiftId,
        int reportVersion,
        string businessDate,
        string shiftKind,
        string filename,
        string contentHash,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateDeviceRequest(connection, HttpMethod.Post, "reports/upload");
        request.Content = JsonContent.Create(new
        {
            shiftId,
            reportVersion,
            businessDate,
            shiftKind,
            filename,
            contentHash,
            byteLength = content.LongLength,
            contentBase64 = Convert.ToBase64String(content)
        }, options: JsonOptions);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var result = await ReadSuccessAsync<ReportUploadResponse>(response, cancellationToken);
        if (result.Id == Guid.Empty || result.Status is not ("accepted" or "duplicate")) throw InvalidResponse(response);
        return result;
    }

    private async Task<CentralEndpointStatus> ProbeAsync(Uri apiBaseUrl, string relativePath, CancellationToken cancellationToken)
    {
        var endpoint = BuildEndpoint(apiBaseUrl, relativePath);
        try
        {
            using var response = await httpClient.GetAsync(endpoint, cancellationToken);
            return new CentralEndpointStatus(
                response.IsSuccessStatusCode,
                (int)response.StatusCode,
                endpoint.ToString(),
                response.IsSuccessStatusCode ? "متصل" : $"الخادم أعاد HTTP {(int)response.StatusCode}");
        }
        catch (HttpRequestException)
        {
            return new CentralEndpointStatus(false, null, endpoint.ToString(), "لا يمكن الوصول إلى الخادم.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CentralEndpointStatus(false, null, endpoint.ToString(), "انتهت مهلة الاتصال بالخادم.");
        }
    }

    private static HttpRequestMessage CreateDeviceRequest(DeviceConnection connection, HttpMethod method, string relativePath)
    {
        if (connection.DeviceId == Guid.Empty || string.IsNullOrWhiteSpace(connection.DeviceSecret))
            throw new ArgumentException("Device credentials are incomplete.", nameof(connection));
        var request = new HttpRequestMessage(method, BuildEndpoint(connection.ApiBaseUrl, relativePath));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-Device-Id", connection.DeviceId.ToString("D"));
        request.Headers.Add("X-Device-Secret", connection.DeviceSecret);
        var version = connection.AppVersion ?? System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3);
        if (!string.IsNullOrWhiteSpace(version)) request.Headers.Add("X-App-Version", version);
        return request;
    }

    public static Uri BuildEndpoint(Uri apiBaseUrl, string relativePath)
    {
        if (!apiBaseUrl.IsAbsoluteUri) throw new ArgumentException("API base URL must be absolute.", nameof(apiBaseUrl));
        var secure = apiBaseUrl.Scheme == Uri.UriSchemeHttps;
        var local = apiBaseUrl.Scheme == Uri.UriSchemeHttp && apiBaseUrl.IsLoopback;
        if (!secure && !local) throw new ArgumentException("HTTPS is required outside localhost.", nameof(apiBaseUrl));
        if (!apiBaseUrl.AbsolutePath.TrimEnd('/').EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("API base URL must end with /api/v1.", nameof(apiBaseUrl));
        return new Uri(apiBaseUrl.ToString().TrimEnd('/') + "/" + relativePath.TrimStart('/'));
    }

    private static async Task<T> ReadSuccessAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            ApiErrorWire? error = null;
            try { error = await response.Content.ReadFromJsonAsync<ApiErrorWire>(JsonOptions, cancellationToken); }
            catch (JsonException) { }
            catch (NotSupportedException) { }
            throw new CentralApiException(
                error?.Code ?? $"HTTP_{(int)response.StatusCode}",
                error?.Message ?? "تعذر تنفيذ الطلب على الخادم.",
                error?.Retryable ?? (int)response.StatusCode is 408 or 429 or >= 500,
                (int)response.StatusCode,
                error?.CorrelationId);
        }
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
                ?? throw new JsonException("Response body is empty.");
        }
        catch (JsonException exception)
        {
            throw new CentralApiException("INVALID_RESPONSE", "وصل رد غير مكتمل من الخادم.", true, (int)response.StatusCode, innerException: exception);
        }
    }

    private static CentralApiException InvalidResponse(HttpResponseMessage response) =>
        new("INVALID_RESPONSE", "وصل رد غير مكتمل أو غير متوافق من الخادم.", true, (int)response.StatusCode);

    private sealed record EnrollmentWireResponse(
        [property: JsonPropertyName("device")] EnrollmentDeviceWire Device,
        [property: JsonPropertyName("credential")] string Credential,
        [property: JsonPropertyName("contractVersion")] string? ContractVersion);
    private sealed record EnrollmentDeviceWire(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("siteId")] Guid SiteId,
        [property: JsonPropertyName("profile")] string Profile,
        [property: JsonPropertyName("streamEpoch")] int StreamEpoch);
    private sealed record ApiErrorWire(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("correlation_id")] string? CorrelationId,
        [property: JsonPropertyName("retryable")] bool Retryable);
}
