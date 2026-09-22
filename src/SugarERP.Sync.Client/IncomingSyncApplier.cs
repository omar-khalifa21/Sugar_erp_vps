using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SugarERP.Application;
using SugarERP.Domain;
using SugarERP.Infrastructure.Local;

namespace SugarERP.Sync.Client;

internal static class IncomingSyncApplier
{
    public static async Task<int> ApplyCatalogSnapshotAsync(
        LocalDatabase database,
        DeviceConfiguration configuration,
        JsonElement bootstrap,
        CancellationToken cancellationToken)
    {
        if (!bootstrap.TryGetProperty("site", out var site)
            || !site.TryGetProperty("id", out var siteIdValue)
            || !siteIdValue.TryGetGuid(out var siteId)
            || siteId != configuration.SiteId
            || !bootstrap.TryGetProperty("catalog", out var catalog)
            || catalog.ValueKind != JsonValueKind.Array)
            throw new CentralApiException("INVALID_BOOTSTRAP", "وصلت بيانات تأسيس لا تخص هذا الفرع.", false, 502);

        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var changed = 0;
            foreach (var row in catalog.EnumerateArray())
            {
                var id = row.GetProperty("id").GetGuid();
                var version = row.GetProperty("version").GetInt32();
                var sku = row.GetProperty("sku").GetString() ?? throw InvalidPayload();
                var name = row.GetProperty("nameAr").GetString() ?? throw InvalidPayload();
                var unit = row.GetProperty("unit").GetString() ?? throw InvalidPayload();
                var scale = row.GetProperty("quantityScale").GetInt32();
                var price = row.GetProperty("retailPriceMinor").GetInt64();
                var active = row.GetProperty("active").GetBoolean();
                var item = await db.CatalogItems.Include(value => value.StockBalance)
                    .SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
                // Bootstrap is authoritative for this site's price. Reapply an
                // equal-version snapshot so a site-price change is not hidden
                // behind the global catalog version.
                if (item is not null && version < item.Version) continue;
                if (item is not null && version == item.Version
                    && item.Sku == sku && item.NameAr == name && item.Unit == unit
                    && item.QuantityScale == scale && item.RetailPriceMinor == price
                    && item.Active == active) continue;
                if (item is null)
                {
                    item = new CatalogItem { Id = id };
                    db.CatalogItems.Add(item);
                    db.StockBalances.Add(new StockBalance
                    {
                        ItemId = id,
                        QuantityScaled = 0,
                        Revision = 1,
                        AsOfUtc = DateTimeOffset.UtcNow
                    });
                }
                else
                {
                    var used = await db.StockMovements.AnyAsync(value => value.ItemId == id, cancellationToken);
                    if (used && (item.QuantityScale != scale || !string.Equals(item.Unit, unit, StringComparison.Ordinal)))
                        throw new CentralApiException("LOCKED_ITEM_PRECISION", "رفض البرنامج تغيير وحدة أو دقة صنف مستخدم في حركات سابقة.", false, 409);
                }
                item.Sku = sku;
                item.NameAr = name;
                item.Unit = unit;
                item.QuantityScale = scale;
                item.RetailPriceMinor = price;
                item.Active = active;
                item.Version = version;
                item.UpdatedAtUtc = DateTimeOffset.UtcNow;
                if (item.QuantityScale < 1 || item.RetailPriceMinor < 0 || item.Version < 1) throw InvalidPayload();
                changed += 1;
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return changed;
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new CentralApiException("INVALID_BOOTSTRAP", "بيانات كتالوج الخادم غير مكتملة.", false, 502, innerException: exception);
        }
        finally
        {
            database.WriteLock.Release();
        }
    }

    public static async Task<int> ApplyPageAsync(
        LocalDatabase database,
        DeviceConfiguration configuration,
        SyncPullResponse page,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(page.ContractVersion, "1.0", StringComparison.Ordinal)
            || !string.Equals(page.Compatibility.Minimum, "1.0", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(page.Cursor)
            || page.Events.Length > 100)
            throw new CentralApiException("INCOMPATIBLE_CONTRACT", "إصدار مزامنة الخادم غير متوافق مع البرنامج.", false, 409);

        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var cursor = await db.SyncCursors.SingleOrDefaultAsync(value => value.FeedScope == "device", cancellationToken);
            if (cursor is null)
            {
                cursor = new SyncCursor { FeedScope = "device" };
                db.SyncCursors.Add(cursor);
            }

            long previousPosition = -1;
            var applied = 0;
            foreach (var incoming in page.Events)
            {
                var addressedKitchenEvent = incoming.EventType is "shipment.dispatched" or "kitchen_request.received"
                    && incoming.OriginSiteId != configuration.SiteId
                    && incoming.Payload.TryGetProperty("destination_site_id", out var destination)
                    && destination.ValueKind == JsonValueKind.String
                    && Guid.TryParse(destination.GetString(), out var destinationSiteId)
                    && destinationSiteId == configuration.SiteId;
                var globalCatalogEvent = incoming.EventType is "catalog.item_published" or "catalog.item.updated" or "catalog.item.deleted";
                var sharedCafeEvent = configuration.Profile == DeviceProfile.BranchType2
                    && incoming.EventType.StartsWith("cafe_customer.", StringComparison.Ordinal);
                if (incoming.Id == Guid.Empty
                    || (incoming.OriginSiteId != configuration.SiteId && !addressedKitchenEvent && !globalCatalogEvent && !sharedCafeEvent)
                    || incoming.DeviceSequence < 1
                    || incoming.SchemaVersion < 1
                    || !long.TryParse(incoming.ServerPosition, out var position)
                    || position <= previousPosition)
                    throw new CentralApiException("INVALID_PULL_RESPONSE", "وصلت صفحة مزامنة غير مرتبة أو خارج نطاق الفرع.", true, 502);
                previousPosition = position;
                var computed = ContractEventFactory.ComputeHash(
                    incoming.Id,
                    incoming.DeviceSequence,
                    incoming.EventType,
                    incoming.SchemaVersion,
                    incoming.OccurredAt,
                    incoming.Payload,
                    incoming.Dependencies);
                if (!string.Equals(computed, incoming.ContentHash, StringComparison.OrdinalIgnoreCase))
                    throw new CentralApiException("CONTENT_HASH_MISMATCH", "فشل التحقق من سلامة حدث نازل من الخادم.", false, 422);

                var existing = await db.InboxMessages.SingleOrDefaultAsync(value => value.EventId == incoming.Id, cancellationToken);
                if (existing is not null)
                {
                    if (!string.Equals(existing.ContentHash, incoming.ContentHash, StringComparison.OrdinalIgnoreCase))
                        throw new CentralApiException("IDEMPOTENCY_KEY_REUSE", "تعارضت هوية حدث نازل مع محتوى سبق تطبيقه.", false, 409);
                    continue;
                }

                if (incoming.OriginDeviceId != configuration.DeviceId)
                    await ApplyBusinessEventAsync(db, incoming, configuration, cancellationToken);
                db.InboxMessages.Add(new InboxMessage
                {
                    EventId = incoming.Id,
                    ContentHash = incoming.ContentHash.ToLowerInvariant(),
                    AppliedAtUtc = DateTimeOffset.UtcNow
                });
                applied += 1;
            }
            cursor.Cursor = page.Cursor;
            cursor.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return applied;
        }
        finally
        {
            database.WriteLock.Release();
        }
    }

    private static async Task ApplyBusinessEventAsync(
        BranchDbContext db,
        SyncPulledEvent incoming,
        DeviceConfiguration configuration,
        CancellationToken cancellationToken)
    {
        switch (incoming.EventType)
        {
            case "catalog.item_published":
            case "catalog.item.updated":
                await ApplyCatalogItemAsync(db, incoming.Payload, configuration, cancellationToken);
                break;
            case "shipment.dispatched":
                await ApplyShipmentAsync(db, incoming, cancellationToken);
                break;
            case "kitchen_request.updated":
            case "kitchen_request.approved":
            case "kitchen_request.rejected":
            case "kitchen_request.received":
                await ApplyRequestUpdateAsync(db, incoming.Payload, cancellationToken);
                break;
            case "quantity_conflict.decided":
                await ApplyConflictDecisionAsync(db, incoming, cancellationToken);
                break;
            case "kitchen_return.acknowledged":
                await ApplyReturnAcknowledgementAsync(db, incoming.Payload, cancellationToken);
                break;
            case "stock.projection_published":
                await ApplyRemoteStockProjectionAsync(db, incoming.Payload, cancellationToken);
                break;
            case "device.bootstrap":
                await ApplyBootstrapAsync(db, incoming, configuration, cancellationToken);
                break;
        }
    }

    private static async Task ApplyCatalogItemAsync(
        BranchDbContext db,
        JsonElement payload,
        DeviceConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var id = RequiredGuid(payload, "item_id");
        var sku = RequiredString(payload, "sku");
        var name = RequiredString(payload, "name_ar");
        var unit = RequiredString(payload, "unit");
        var scale = RequiredInt(payload, "quantity_scale");
        var price = RequiredLong(payload, "retail_price_minor");
        var version = RequiredInt(payload, "version");
        var active = OptionalBoolean(payload, "active", true);
        var priceSiteId = OptionalGuid(payload, "price_site_id") ?? OptionalGuid(payload, "site_id");
        var appliesLocalPrice = priceSiteId is null || priceSiteId == configuration.SiteId;
        if (scale < 1 || price < 0 || version < 1) throw InvalidPayload();
        var item = await db.CatalogItems.Include(value => value.StockBalance).SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (item is null)
        {
            item = new CatalogItem
            {
                Id = id,
                Sku = sku,
                NameAr = name,
                Unit = unit,
                QuantityScale = scale,
                RetailPriceMinor = appliesLocalPrice ? price : 0,
                Active = active,
                Version = version,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            db.CatalogItems.Add(item);
            db.StockBalances.Add(new StockBalance { ItemId = id, QuantityScaled = 0, Revision = 1, AsOfUtc = DateTimeOffset.UtcNow });
            return;
        }
        if (version < item.Version) return;
        if (version == item.Version)
        {
            // The server publishes one catalog event per target site. Those
            // events intentionally share the global item version, so the
            // event addressed to this branch must still be allowed to update
            // its independent retail price.
            if (appliesLocalPrice) item.RetailPriceMinor = price;
            return;
        }
        var used = await db.StockMovements.AnyAsync(value => value.ItemId == id, cancellationToken);
        if (used && (item.QuantityScale != scale || !string.Equals(item.Unit, unit, StringComparison.Ordinal)))
            throw new CentralApiException("LOCKED_ITEM_PRECISION", "رفض البرنامج تغيير وحدة أو دقة صنف مستخدم في حركات سابقة.", false, 409);
        item.Sku = sku;
        item.NameAr = name;
        item.Unit = unit;
        item.QuantityScale = scale;
        if (appliesLocalPrice) item.RetailPriceMinor = price;
        item.Active = active;
        item.Version = version;
        item.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static async Task ApplyShipmentAsync(BranchDbContext db, SyncPulledEvent incoming, CancellationToken cancellationToken)
    {
        var payload = incoming.Payload;
        var shipmentId = RequiredGuid(payload, "shipment_id");
        var version = RequiredInt(payload, "version");
        var existing = await db.Shipments.Include(value => value.Lines).SingleOrDefaultAsync(value => value.Id == shipmentId, cancellationToken);
        if (existing is not null)
        {
            if (existing.Version > version) return;
            if (existing.Version == version) return;
            throw new CentralApiException("STALE_VERSION", "تعارض إصدار طلب وارد سبق تنزيله.", false, 409);
        }
        if (!payload.TryGetProperty("lines", out var linesElement) || linesElement.ValueKind != JsonValueKind.Array)
            throw InvalidPayload();
        var shipment = new Shipment
        {
            Id = shipmentId,
            CommandId = incoming.Id,
            RequestId = OptionalGuid(payload, "request_id"),
            Reference = RequiredString(payload, "reference"),
            Status = ShipmentStatus.AwaitingReceipt,
            Version = version,
            DispatchedAtUtc = ParseDate(RequiredString(payload, "dispatched_at")),
        };
        foreach (var row in linesElement.EnumerateArray())
        {
            var line = new ShipmentLine
            {
                Id = RequiredGuid(row, "line_id"),
                ShipmentId = shipment.Id,
                RequestLineId = OptionalGuid(row, "request_line_id"),
                ItemId = RequiredGuid(row, "item_id"),
                NameSnapshot = RequiredString(row, "name_snapshot"),
                UnitSnapshot = RequiredString(row, "unit_snapshot"),
                QuantityScale = RequiredInt(row, "quantity_scale"),
                SentScaled = RequiredLong(row, "sent_scaled")
            };
            if (line.QuantityScale < 1 || line.SentScaled <= 0) throw InvalidPayload();
            shipment.Lines.Add(line);
        }
        if (shipment.Lines.Count == 0 || shipment.Lines.Select(value => value.ItemId).Distinct().Count() != shipment.Lines.Count)
            throw InvalidPayload();
        db.Shipments.Add(shipment);

        if (shipment.RequestId is Guid requestId)
        {
            var request = await db.KitchenRequests.Include(value => value.Lines)
                .SingleOrDefaultAsync(value => value.Id == requestId, cancellationToken);
            if (request is null)
                throw new CentralApiException("DEPENDENCY_NOT_READY", "وصلت شحنة لطلب وارد غير موجود محلياً.", true, 409);
            foreach (var line in shipment.Lines)
            {
                if (line.RequestLineId is not Guid requestLineId) throw InvalidPayload();
                var requestLine = request.Lines.SingleOrDefault(value => value.Id == requestLineId)
                    ?? throw InvalidPayload();
                requestLine.SentScaled = checked(requestLine.SentScaled + line.SentScaled);
            }
            var finalized = incoming.Payload.TryGetProperty("finalized", out var finalizedValue)
                && finalizedValue.ValueKind == JsonValueKind.True;
            request.Status = finalized || request.Lines.All(value => value.SentScaled >= value.RequestedScaled)
                ? KitchenRequestStatus.Fulfilled
                : KitchenRequestStatus.Partial;
            request.Version = checked(request.Version + 1);
        }
    }

    private static async Task ApplyRequestUpdateAsync(BranchDbContext db, JsonElement payload, CancellationToken cancellationToken)
    {
        var requestId = RequiredGuid(payload, "request_id");
        var version = RequiredInt(payload, "version");
        var request = await db.KitchenRequests.Include(value => value.Lines).SingleOrDefaultAsync(value => value.Id == requestId, cancellationToken);
        if (request is null) throw new CentralApiException("DEPENDENCY_NOT_READY", "وصل تحديث لطلب وارد غير موجود محلياً.", true, 409);
        if (version <= request.Version) return;
        request.Status = RequiredString(payload, "status") switch
        {
            "RECEIVED" => KitchenRequestStatus.Received,
            "APPROVED" => KitchenRequestStatus.Approved,
            "REJECTED" => KitchenRequestStatus.Rejected,
            "PARTIAL" => KitchenRequestStatus.Partial,
            "FULFILLED" => KitchenRequestStatus.Fulfilled,
            "CLOSED" => KitchenRequestStatus.Closed,
            _ => throw InvalidPayload()
        };
        request.Version = version;
        if (payload.TryGetProperty("lines", out var lines) && lines.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in lines.EnumerateArray())
            {
                var lineId = RequiredGuid(row, "request_line_id");
                var line = request.Lines.SingleOrDefault(value => value.Id == lineId) ?? throw InvalidPayload();
                line.ApprovedScaled = OptionalLong(row, "approved_scaled");
                line.SentScaled = OptionalLong(row, "sent_scaled") ?? line.SentScaled;
            }
        }
    }

    private static async Task ApplyConflictDecisionAsync(BranchDbContext db, SyncPulledEvent incoming, CancellationToken cancellationToken)
    {
        var payload = incoming.Payload;
        var conflictId = RequiredGuid(payload, "conflict_id");
        var conflict = await db.QuantityConflicts.Include(value => value.Lines)
            .SingleOrDefaultAsync(value => value.Id == conflictId, cancellationToken)
            ?? throw new CentralApiException("DEPENDENCY_NOT_READY", "وصل قرار لكمية وارد غير موجودة محلياً.", true, 409);
        if (conflict.Status == "RESOLVED") return;
        var shift = await db.Shifts.SingleOrDefaultAsync(value => value.Status == ShiftStatus.Open, cancellationToken)
            ?? throw new CentralApiException("DEPENDENCY_NOT_READY", "قرار طلب وارد ينتظر فتح وردية لتطبيق المخزون.", true, 409);
        if (!payload.TryGetProperty("lines", out var decisions) || decisions.ValueKind != JsonValueKind.Array)
            throw InvalidPayload();
        var decisionRows = decisions.EnumerateArray().ToArray();
        if (decisionRows.Length != conflict.Lines.Count) throw InvalidPayload();
        var receipt = await db.IncomingReceipts.Include(value => value.Lines).SingleAsync(value => value.Id == conflict.ReceiptId, cancellationToken);
        foreach (var decision in decisionRows)
        {
            var receiptLineId = RequiredGuid(decision, "receipt_line_id");
            var finalScaled = RequiredLong(decision, "final_scaled");
            if (finalScaled < 0) throw InvalidPayload();
            var receiptLine = receipt.Lines.SingleOrDefault(value => value.Id == receiptLineId) ?? throw InvalidPayload();
            var hold = await db.StockHolds.SingleAsync(value => value.ReceiptLineId == receiptLineId, cancellationToken);
            if (hold.Status == HoldStatus.Released) continue;
            if (finalScaled > 0)
            {
                var balance = await db.StockBalances.SingleAsync(value => value.ItemId == receiptLine.ItemId, cancellationToken);
                balance.QuantityScaled += finalScaled;
                balance.Revision += 1;
                balance.AsOfUtc = DateTimeOffset.UtcNow;
                var snapshot = await GetOrCreateSnapshotAsync(db, shift, receiptLine.ItemId, cancellationToken);
                snapshot.IncomingScaled += finalScaled;
                snapshot.ExpectedCloseScaled += finalScaled;
                db.StockMovements.Add(new StockMovement
                {
                    Id = Guid.NewGuid(),
                    DocumentId = receipt.Id,
                    SourceLineId = receiptLine.Id,
                    ItemId = receiptLine.ItemId,
                    ShiftId = shift.Id,
                    Kind = StockMovementKind.IncomingReceipt,
                    DeltaScaled = finalScaled,
                    OccurredAtUtc = DateTimeOffset.UtcNow
                });
            }
            receiptLine.ConfirmedScaled = finalScaled;
            hold.ReleasedScaled = finalScaled;
            hold.Status = HoldStatus.Released;
            hold.DecisionId = OptionalGuid(payload, "decision_id");
        }
        receipt.Status = IncomingReceiptStatus.Resolved;
        receipt.AcceptedAtUtc = DateTimeOffset.UtcNow;
        conflict.Status = "RESOLVED";
        var shipment = await db.Shipments.SingleAsync(value => value.Id == receipt.ShipmentId, cancellationToken);
        shipment.Status = ShipmentStatus.Resolved;
        shipment.Version += 1;
        var sequence = await GetSequenceAsync(db, cancellationToken);
        QueueAppliedEvent(db, sequence, conflict.Id, incoming.Id, receipt, shift.Id);
    }

    private static async Task ApplyReturnAcknowledgementAsync(BranchDbContext db, JsonElement payload, CancellationToken cancellationToken)
    {
        var returnId = RequiredGuid(payload, "return_id");
        var entity = await db.KitchenReturns.SingleOrDefaultAsync(value => value.Id == returnId, cancellationToken)
            ?? throw new CentralApiException("DEPENDENCY_NOT_READY", "وصل إقرار مرتجع مطبخ غير موجود محلياً.", true, 409);
        if (entity.Status == KitchenReturnStatus.Acknowledged) return;
        entity.Status = RequiredString(payload, "status") switch
        {
            "ACKNOWLEDGED" => KitchenReturnStatus.Acknowledged,
            "DISPUTED" => KitchenReturnStatus.Disputed,
            _ => throw InvalidPayload()
        };
    }

    private static async Task ApplyRemoteStockProjectionAsync(BranchDbContext db, JsonElement payload, CancellationToken cancellationToken)
    {
        var siteId = RequiredGuid(payload, "site_id");
        var itemId = RequiredGuid(payload, "item_id");
        var projection = await db.RemoteStockProjections.SingleOrDefaultAsync(value => value.SiteId == siteId && value.ItemId == itemId, cancellationToken);
        if (projection is null)
        {
            projection = new RemoteStockProjection { SiteId = siteId, ItemId = itemId };
            db.RemoteStockProjections.Add(projection);
        }
        projection.SiteName = RequiredString(payload, "site_name");
        projection.ItemName = RequiredString(payload, "item_name");
        projection.Unit = RequiredString(payload, "unit");
        projection.QuantityScale = RequiredInt(payload, "quantity_scale");
        projection.QuantityScaled = RequiredLong(payload, "quantity_scaled");
        projection.AsOfUtc = ParseDate(RequiredString(payload, "as_of"));
    }

    private static async Task ApplyBootstrapAsync(
        BranchDbContext db,
        SyncPulledEvent incoming,
        DeviceConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (incoming.Payload.TryGetProperty("catalog", out var catalog) && catalog.ValueKind == JsonValueKind.Array)
            foreach (var item in catalog.EnumerateArray()) await ApplyCatalogItemAsync(db, item, configuration, cancellationToken);
        if (incoming.Payload.TryGetProperty("shipments", out var shipments) && shipments.ValueKind == JsonValueKind.Array)
        {
            foreach (var shipment in shipments.EnumerateArray())
            {
                var syntheticEvent = incoming with { Payload = shipment, EventType = "shipment.dispatched" };
                await ApplyShipmentAsync(db, syntheticEvent, cancellationToken);
            }
        }
    }

    private static async Task<ShiftItemSnapshot> GetOrCreateSnapshotAsync(BranchDbContext db, Shift shift, Guid itemId, CancellationToken cancellationToken)
    {
        var snapshot = await db.ShiftItemSnapshots.SingleOrDefaultAsync(value => value.ShiftId == shift.Id && value.ItemId == itemId, cancellationToken);
        if (snapshot is not null) return snapshot;
        var item = await db.CatalogItems.Include(value => value.StockBalance).SingleAsync(value => value.Id == itemId, cancellationToken);
        snapshot = new ShiftItemSnapshot
        {
            ShiftId = shift.Id,
            ItemId = itemId,
            NameSnapshot = item.NameAr,
            UnitSnapshot = item.Unit,
            QuantityScale = item.QuantityScale,
            OpeningQuantityScaled = item.StockBalance?.QuantityScaled ?? 0,
            ExpectedCloseScaled = item.StockBalance?.QuantityScaled ?? 0
        };
        db.ShiftItemSnapshots.Add(snapshot);
        return snapshot;
    }

    private static async Task<SequenceState> GetSequenceAsync(BranchDbContext db, CancellationToken cancellationToken)
    {
        var sequence = await db.SequenceStates.SingleOrDefaultAsync(cancellationToken);
        if (sequence is not null) return sequence;
        sequence = new SequenceState();
        db.SequenceStates.Add(sequence);
        return sequence;
    }

    private static void QueueAppliedEvent(BranchDbContext db, SequenceState sequence, Guid conflictId, Guid decisionEventId, IncomingReceipt receipt, Guid shiftId)
    {
        var now = DateTimeOffset.UtcNow;
        var contractEvent = ContractEventFactory.Create(
            Guid.NewGuid(),
            sequence.NextDeviceSequence,
            "quantity_conflict.applied",
            now,
            new
            {
                conflict_id = conflictId,
                decision_event_id = decisionEventId,
                receipt_id = receipt.Id,
                posting_shift_id = shiftId,
                lines = receipt.Lines.Select(value => new { receipt_line_id = value.Id, confirmed_scaled = value.ConfirmedScaled }).ToArray()
            },
            [decisionEventId]);
        sequence.NextDeviceSequence += 1;
        db.OutboxMessages.Add(new OutboxMessage
        {
            EventId = contractEvent.Id,
            AggregateId = conflictId,
            DeviceSequence = contractEvent.DeviceSequence,
            EventType = contractEvent.EventType,
            SchemaVersion = contractEvent.SchemaVersion,
            OccurredAtUtc = contractEvent.OccurredAtUtc,
            PayloadJson = contractEvent.PayloadJson,
            DependenciesJson = contractEvent.DependenciesJson,
            ContentHash = contractEvent.ContentHash,
            State = OutboxState.Pending,
            NextAttemptAtUtc = now
        });
    }

    private static Guid RequiredGuid(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var parsed)
            ? parsed
            : throw InvalidPayload();

    private static Guid? OptionalGuid(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;

    private static string RequiredString(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw InvalidPayload();

    private static int RequiredInt(JsonElement payload, string name) => checked((int)RequiredLong(payload, name));

    private static long RequiredLong(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.TryGetInt64(out var parsed) ? parsed : throw InvalidPayload();

    private static long? OptionalLong(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null && value.TryGetInt64(out var parsed) ? parsed : null;

    private static bool OptionalBoolean(JsonElement payload, string name, bool fallback) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed.ToUniversalTime() : throw InvalidPayload();

    private static CentralApiException InvalidPayload() =>
        new("INVALID_EVENT_PAYLOAD", "وصل حدث ناقص أو غير صالح من الخادم. لم يتم تطبيق الصفحة.", false, 422);
}
