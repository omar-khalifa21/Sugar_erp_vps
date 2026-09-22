using Microsoft.EntityFrameworkCore;
using System.Text;
using SugarERP.Application;
using SugarERP.Domain;

namespace SugarERP.Infrastructure.Local;

public sealed class BranchModuleOperationsService(LocalDatabase database) : IBranchModuleOperations
{
    public async Task<CatalogModuleSnapshot> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        await using var db = database.CreateContext();
        var items = await db.CatalogItems.AsNoTracking()
            .Include(value => value.StockBalance)
            .OrderBy(value => value.NameAr)
            .Select(value => new CatalogItemSnapshot(
                value.Id,
                value.Sku,
                value.NameAr,
                value.Unit,
                value.QuantityScale,
                value.RetailPriceMinor,
                value.Active,
                value.StockBalance == null ? 0 : value.StockBalance.QuantityScaled,
                value.StockBalance == null ? 0 : value.StockBalance.Revision,
                value.Version))
            .ToListAsync(cancellationToken);
        var names = items.ToDictionary(value => value.Id, value => value.NameAr);
        var units = items.ToDictionary(value => value.Id, value => value.Unit);
        var scales = items.ToDictionary(value => value.Id, value => value.QuantityScale);
        var holds = await db.StockHolds.AsNoTracking()
            .Where(value => value.Status != HoldStatus.Released)
            .OrderByDescending(value => value.Id)
            .ToListAsync(cancellationToken);
        var holdSnapshots = holds.Select(value => new StockHoldSnapshot(
            value.Id,
            value.ItemId,
            names.GetValueOrDefault(value.ItemId, "صنف غير متاح"),
            units.GetValueOrDefault(value.ItemId, string.Empty),
            scales.GetValueOrDefault(value.ItemId, 1),
            value.PhysicalCountScaled,
            value.Status.ToString())).ToArray();
        var remote = await db.RemoteStockProjections.AsNoTracking()
            .OrderBy(value => value.SiteName)
            .ThenBy(value => value.ItemName)
            .Select(value => new RemoteStockSnapshot(
                value.SiteId,
                value.SiteName,
                value.ItemId,
                value.ItemName,
                value.Unit,
                value.QuantityScale,
                value.QuantityScaled,
                value.AsOfUtc))
            .ToListAsync(cancellationToken);
        var appliedDates = await db.InboxMessages.AsNoTracking().Select(value => value.AppliedAtUtc).ToListAsync(cancellationToken);
        return new CatalogModuleSnapshot(items, holdSnapshots, remote, appliedDates.Count == 0 ? null : appliedDates.Max());
    }

    public async Task<CatalogItemSnapshot> SaveCatalogItemAsync(
        SaveCatalogItemCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateCommandId(command.CommandId);
        var sku = command.Sku.Trim();
        var name = command.NameAr.Trim();
        var unit = command.Unit.Trim();
        if (string.IsNullOrWhiteSpace(sku) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(unit))
            throw Rule("CATALOG_FIELDS_REQUIRED", "اكتب كود الصنف واسمه ووحدته.");
        if (command.QuantityScale is < 1 or > 1000)
            throw Rule("INVALID_QUANTITY_SCALE", "دقة الوحدة يجب أن تكون بين 1 و1000.");
        if (command.RetailPriceMinor < 0)
            throw Rule("INVALID_PRICE", "سعر البيع لا يمكن أن يكون سالباً.");

        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            var replay = await db.OutboxMessages.AsNoTracking().SingleOrDefaultAsync(value => value.EventId == command.CommandId, cancellationToken);
            if (replay is not null)
            {
                var replayItem = await db.CatalogItems.AsNoTracking().Include(value => value.StockBalance)
                    .SingleAsync(value => value.Id == replay.AggregateId, cancellationToken);
                if (replayItem.Sku != sku || replayItem.NameAr != name || replayItem.Unit != unit
                    || replayItem.QuantityScale != command.QuantityScale || replayItem.RetailPriceMinor != command.RetailPriceMinor)
                    throw IdempotencyReuse();
                return ToCatalogSnapshot(replayItem);
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var configuration = await RequireBranchConfigurationAsync(db, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            CatalogItem item;
            if (command.ItemId is null)
            {
                item = new CatalogItem
                {
                    Id = Guid.NewGuid(),
                    Sku = sku,
                    NameAr = name,
                    Unit = unit,
                    QuantityScale = command.QuantityScale,
                    RetailPriceMinor = command.RetailPriceMinor,
                    Active = true,
                    Version = 1,
                    UpdatedAtUtc = now,
                    StockBalance = new StockBalance { QuantityScaled = 0, Revision = 1, AsOfUtc = now }
                };
                db.CatalogItems.Add(item);
            }
            else
            {
                item = await db.CatalogItems.Include(value => value.StockBalance)
                    .SingleOrDefaultAsync(value => value.Id == command.ItemId.Value, cancellationToken)
                    ?? throw Rule("ITEM_NOT_FOUND", "لم يتم العثور على الصنف.");
                if (command.ExpectedVersion is null || item.Version != command.ExpectedVersion)
                    throw Rule("STALE_VERSION", "تم تحديث الصنف. افتح بياناته من جديد ثم حاول مرة أخرى.");
                var hasPostedMovement = await db.StockMovements.AsNoTracking().AnyAsync(value => value.ItemId == item.Id, cancellationToken);
                if (hasPostedMovement && (item.Unit != unit || item.QuantityScale != command.QuantityScale))
                    throw Rule("UNIT_SCALE_LOCKED", "لا يمكن تغيير الوحدة أو دقتها بعد استخدام الصنف في حركة مخزون.");
                item.Sku = sku;
                item.NameAr = name;
                item.Unit = unit;
                item.QuantityScale = command.QuantityScale;
                item.RetailPriceMinor = command.RetailPriceMinor;
                item.Active = true;
                item.Version += 1;
                item.UpdatedAtUtc = now;
            }

            var sequence = await GetSequenceAsync(db, cancellationToken);
            QueueEvent(db, sequence, item.Id, "catalog.item.updated", now, new
            {
                item_id = item.Id,
                site_id = configuration.SiteId,
                sku = item.Sku,
                name_ar = item.NameAr,
                unit = item.Unit,
                quantity_scale = item.QuantityScale,
                retail_price_minor = item.RetailPriceMinor,
                active = item.Active,
                version = item.Version
            }, command.CommandId);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                throw Rule("SKU_ALREADY_EXISTS", "كود الصنف مستخدم لصنف آخر.");
            }
            await transaction.CommitAsync(cancellationToken);
            return ToCatalogSnapshot(item);
        }
        finally
        {
            database.WriteLock.Release();
        }
    }

    public async Task<CatalogItemSnapshot> ArchiveCatalogItemAsync(
        Guid commandId,
        Guid itemId,
        int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ValidateCommandId(commandId);
        if (itemId == Guid.Empty || expectedVersion < 1)
            throw Rule("INVALID_ITEM", "الصنف المحدد غير صالح.");
        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            var replay = await db.OutboxMessages.AsNoTracking().SingleOrDefaultAsync(value => value.EventId == commandId, cancellationToken);
            if (replay is not null)
            {
                if (replay.AggregateId != itemId) throw IdempotencyReuse();
                var archived = await db.CatalogItems.AsNoTracking().Include(value => value.StockBalance)
                    .SingleAsync(value => value.Id == itemId, cancellationToken);
                return ToCatalogSnapshot(archived);
            }
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var configuration = await RequireBranchConfigurationAsync(db, cancellationToken);
            var item = await db.CatalogItems.Include(value => value.StockBalance)
                .SingleOrDefaultAsync(value => value.Id == itemId, cancellationToken)
                ?? throw Rule("ITEM_NOT_FOUND", "لم يتم العثور على الصنف.");
            if (item.Version != expectedVersion)
                throw Rule("STALE_VERSION", "تم تحديث الصنف. افتح بياناته من جديد ثم حاول مرة أخرى.");
            if (!item.Active) return ToCatalogSnapshot(item);
            var now = DateTimeOffset.UtcNow;
            item.Active = false;
            item.Version += 1;
            item.UpdatedAtUtc = now;
            var sequence = await GetSequenceAsync(db, cancellationToken);
            QueueEvent(db, sequence, item.Id, "catalog.item.updated", now, new
            {
                item_id = item.Id,
                site_id = configuration.SiteId,
                sku = item.Sku,
                name_ar = item.NameAr,
                unit = item.Unit,
                quantity_scale = item.QuantityScale,
                retail_price_minor = item.RetailPriceMinor,
                active = false,
                version = item.Version
            }, commandId);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToCatalogSnapshot(item);
        }
        finally
        {
            database.WriteLock.Release();
        }
    }

    public async Task DeleteCatalogItemAsync(
        Guid commandId,
        Guid itemId,
        int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ValidateCommandId(commandId);
        if (itemId == Guid.Empty || expectedVersion < 1)
            throw Rule("INVALID_ITEM", "الصنف المحدد غير صالح.");
        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            var replay = await db.OutboxMessages.AsNoTracking().SingleOrDefaultAsync(value => value.EventId == commandId, cancellationToken);
            if (replay is not null)
            {
                if (replay.AggregateId != itemId || replay.EventType != "catalog.item.deleted") throw IdempotencyReuse();
                return;
            }
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var configuration = await RequireBranchConfigurationAsync(db, cancellationToken);
            var item = await db.CatalogItems.Include(value => value.StockBalance)
                .SingleOrDefaultAsync(value => value.Id == itemId, cancellationToken)
                ?? throw Rule("ITEM_NOT_FOUND", "الصنف محذوف بالفعل أو غير موجود.");
            if (item.Version != expectedVersion)
                throw Rule("STALE_VERSION", "تم تحديث الصنف. افتح بياناته من جديد ثم حاول مرة أخرى.");
            var hasReferences = (item.StockBalance?.QuantityScaled ?? 0) != 0
                || await db.StockMovements.AsNoTracking().AnyAsync(value => value.ItemId == itemId, cancellationToken)
                || await db.SaleLines.AsNoTracking().AnyAsync(value => value.ItemId == itemId, cancellationToken)
                || await db.KitchenRequestLines.AsNoTracking().AnyAsync(value => value.ItemId == itemId, cancellationToken)
                || await db.ShipmentLines.AsNoTracking().AnyAsync(value => value.ItemId == itemId, cancellationToken)
                || await db.KitchenReturnLines.AsNoTracking().AnyAsync(value => value.ItemId == itemId, cancellationToken)
                || await db.ShiftItemSnapshots.AsNoTracking().AnyAsync(value => value.ItemId == itemId, cancellationToken);
            if (hasReferences)
                throw Rule("ITEM_HAS_HISTORY", "لا يمكن حذف صنف له رصيد أو حركات. استخدم الأرشفة حتى يبقى الحساب صحيحاً.");

            var now = DateTimeOffset.UtcNow;
            var sequence = await GetSequenceAsync(db, cancellationToken);
            QueueEvent(db, sequence, item.Id, "catalog.item.deleted", now, new
            {
                item_id = item.Id,
                site_id = configuration.SiteId,
                expected_version = item.Version
            }, commandId);
            if (item.StockBalance is not null) db.StockBalances.Remove(item.StockBalance);
            db.CatalogItems.Remove(item);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            database.WriteLock.Release();
        }
    }

    public async Task<IReadOnlyList<KitchenRequestSnapshot>> GetKitchenRequestsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = database.CreateContext();
        var requests = await db.KitchenRequests.AsNoTracking()
            .Include(value => value.Lines)
            .ToListAsync(cancellationToken);
        var requestIds = requests.Select(value => value.Id).ToArray();
        var deliveryStates = await db.OutboxMessages.AsNoTracking()
            .Where(value => requestIds.Contains(value.AggregateId) && value.EventType == "kitchen_request.submitted")
            .GroupBy(value => value.AggregateId)
            .Select(value => new { RequestId = value.Key, State = value.OrderByDescending(message => message.DeviceSequence).Select(message => message.State).First() })
            .ToDictionaryAsync(value => value.RequestId, value => value.State, cancellationToken);
        return requests.OrderByDescending(value => value.RequestedAtUtc)
            .Select(value => ToRequestSnapshot(value, deliveryState: ResolveDeliveryState(value, deliveryStates.GetValueOrDefault(value.Id))))
            .ToArray();
    }

    public async Task<KitchenRequestSnapshot> CreateKitchenRequestAsync(
        CreateKitchenRequestCommand command,
        bool submit,
        CancellationToken cancellationToken = default)
    {
        ValidateCommandId(command.CommandId);
        ValidateQuantityInputs(command.Lines, "أدخل كمية موجبة لصنف واحد على الأقل.");
        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            var existing = await db.KitchenRequests.Include(value => value.Lines)
                .SingleOrDefaultAsync(value => value.CommandId == command.CommandId, cancellationToken);
            if (existing is not null)
            {
                EnsureSameQuantities(command.Lines, existing.Lines.Select(value => new QuantityInput(value.ItemId, value.RequestedScaled)));
                return ToRequestSnapshot(existing, true);
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var configuration = await RequireBranchConfigurationAsync(db, cancellationToken);
            var itemIds = command.Lines.Select(value => value.ItemId).ToArray();
            var items = await db.CatalogItems.Where(value => itemIds.Contains(value.Id)).ToDictionaryAsync(value => value.Id, cancellationToken);
            if (items.Count != itemIds.Length)
                throw Rule("ITEM_UNAVAILABLE", "أحد الأصناف غير موجود في الكتالوج المحلي. نفّذ المزامنة أولاً.");

            var now = DateTimeOffset.UtcNow;
            var request = new KitchenRequest
            {
                Id = Guid.NewGuid(),
                CommandId = command.CommandId,
                Status = submit ? KitchenRequestStatus.Submitted : KitchenRequestStatus.Draft,
                BusinessDate = CairoBusinessDate(now),
                RequestedAtUtc = now,
                SubmittedAtUtc = submit ? now : null
            };
            foreach (var input in command.Lines)
            {
                var item = items[input.ItemId];
                request.Lines.Add(new KitchenRequestLine
                {
                    Id = Guid.NewGuid(),
                    RequestId = request.Id,
                    ItemId = item.Id,
                    NameSnapshot = item.NameAr,
                    UnitSnapshot = item.Unit,
                    QuantityScale = item.QuantityScale,
                    RequestedScaled = input.QuantityScaled
                });
            }
            db.KitchenRequests.Add(request);
            if (submit)
            {
                var sequence = await GetSequenceAsync(db, cancellationToken);
                QueueEvent(db, sequence, request.Id, "kitchen_request.submitted", now, new
                {
                    request_id = request.Id,
                    requesting_site_id = configuration.SiteId,
                    branch_name = configuration.SiteName,
                    business_date = request.BusinessDate,
                    version = request.Version,
                    lines = request.Lines.Select(value => new
                    {
                        line_id = value.Id,
                        item_id = value.ItemId,
                        requested_scaled = value.RequestedScaled,
                        quantity_scale = value.QuantityScale,
                        name_snapshot = value.NameSnapshot,
                        unit_snapshot = value.UnitSnapshot
                    }).ToArray()
                });
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToRequestSnapshot(request);
        }
        finally
        {
            database.WriteLock.Release();
        }
    }

    public async Task<KitchenRequestSnapshot> SubmitKitchenRequestAsync(
        Guid requestId,
        int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        if (requestId == Guid.Empty || expectedVersion < 1)
            throw Rule("INVALID_REQUEST", "طلب الوارد غير صالح.");
        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var request = await db.KitchenRequests.Include(value => value.Lines)
                .SingleOrDefaultAsync(value => value.Id == requestId, cancellationToken)
                ?? throw Rule("REQUEST_NOT_FOUND", "لم يتم العثور على طلب الوارد.");
            if (request.Status == KitchenRequestStatus.Submitted)
                return ToRequestSnapshot(request, true);
            if (request.Status != KitchenRequestStatus.Draft)
                throw Rule("REQUEST_FROZEN", "لا يمكن تعديل أو إرسال طلب وارد بعد اعتماده أو رفضه.");
            if (request.Version != expectedVersion)
                throw Rule("STALE_VERSION", "تم تحديث طلب الوارد. أعد فتحه ثم حاول مرة أخرى.");

            var configuration = await RequireBranchConfigurationAsync(db, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            request.Status = KitchenRequestStatus.Submitted;
            request.SubmittedAtUtc = now;
            request.Version += 1;
            var sequence = await GetSequenceAsync(db, cancellationToken);
            QueueEvent(db, sequence, request.Id, "kitchen_request.submitted", now, new
            {
                request_id = request.Id,
                requesting_site_id = configuration.SiteId,
                branch_name = configuration.SiteName,
                business_date = request.BusinessDate,
                version = request.Version,
                lines = request.Lines.Select(value => new
                {
                    line_id = value.Id,
                    item_id = value.ItemId,
                    requested_scaled = value.RequestedScaled,
                    quantity_scale = value.QuantityScale,
                    name_snapshot = value.NameSnapshot,
                    unit_snapshot = value.UnitSnapshot
                }).ToArray()
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToRequestSnapshot(request);
        }
        finally
        {
            database.WriteLock.Release();
        }
    }

    public async Task<IReadOnlyList<IncomingShipmentSnapshot>> GetIncomingShipmentsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = database.CreateContext();
        var shipments = await db.Shipments.AsNoTracking()
            .Include(value => value.Lines)
            .Include(value => value.Receipt)
                .ThenInclude(value => value!.Lines)
            .ToListAsync(cancellationToken);
        return shipments.OrderByDescending(value => value.DispatchedAtUtc)
            .Select(ToShipmentSnapshot)
            .ToArray();
    }

    public async Task<IncomingReceiptResult> ReceiveShipmentAsync(
        ReceiveShipmentCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateCommandId(command.CommandId);
        if (command.ShipmentId == Guid.Empty || command.ExpectedVersion < 1 || command.Lines.Count == 0)
            throw Rule("INVALID_RECEIPT", "راجع بيانات عد طلب الوارد.");
        if (command.Lines.Any(value => value.ShipmentLineId == Guid.Empty || value.CountedScaled < 0)
            || command.Lines.Select(value => value.ShipmentLineId).Distinct().Count() != command.Lines.Count)
            throw Rule("INVALID_RECEIPT", "يجب إدخال عد صحيح لكل صنف مرة واحدة.");

        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            var existing = await db.IncomingReceipts.AsNoTracking()
                .Include(value => value.Lines)
                .SingleOrDefaultAsync(value => value.CommandId == command.CommandId, cancellationToken);
            if (existing is not null)
            {
                var same = existing.ShipmentId == command.ShipmentId
                    && existing.Lines.Count == command.Lines.Count
                    && existing.Lines.OrderBy(value => value.ShipmentLineId)
                        .Zip(command.Lines.OrderBy(value => value.ShipmentLineId))
                        .All(value => value.First.ShipmentLineId == value.Second.ShipmentLineId
                            && value.First.CountedScaled == value.Second.CountedScaled);
                if (!same) throw IdempotencyReuse();
                return new IncomingReceiptResult(existing.Id, existing.ShipmentId, existing.Status, existing.Status == IncomingReceiptStatus.Disputed, true);
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var shift = await RequireOpenShiftAsync(db, cancellationToken);
            var shipment = await db.Shipments
                .Include(value => value.Lines)
                .SingleOrDefaultAsync(value => value.Id == command.ShipmentId, cancellationToken)
                ?? throw Rule("SHIPMENT_NOT_FOUND", "لم يتم العثور على طلب الوارد على هذا الجهاز.");
            if (shipment.Version != command.ExpectedVersion)
                throw Rule("STALE_VERSION", "تم تحديث طلب الوارد. أعد فتحه ثم أدخل العد مرة أخرى.");
            if (shipment.Status is not (ShipmentStatus.Dispatched or ShipmentStatus.AwaitingReceipt))
                throw Rule("SHIPMENT_FROZEN", "تم تسجيل استلام طلب الوارد بالفعل ولا يمكن تكراره.");
            if (shipment.Lines.Count != command.Lines.Count
                || shipment.Lines.Select(value => value.Id).Order().SequenceEqual(command.Lines.Select(value => value.ShipmentLineId).Order()) is false)
                throw Rule("INCOMPLETE_COUNT", "أدخل الكمية المعدودة لكل أصناف طلب الوارد، بما فيها الصفر.");

            var counts = command.Lines.ToDictionary(value => value.ShipmentLineId, value => value.CountedScaled);
            var exact = shipment.Lines.All(value => counts[value.Id] == value.SentScaled);
            var now = DateTimeOffset.UtcNow;
            var receipt = new IncomingReceipt
            {
                Id = Guid.NewGuid(),
                CommandId = command.CommandId,
                ShipmentId = shipment.Id,
                ReceivingShiftId = shift.Id,
                Status = exact ? IncomingReceiptStatus.Accepted : IncomingReceiptStatus.Disputed,
                CountedAtUtc = now,
                AcceptedAtUtc = exact ? now : null
            };
            var configuration = await db.DeviceConfigurations.SingleAsync(cancellationToken);
            BranchInventoryTransaction? locationTransaction = null;
            if (configuration.Profile == DeviceProfile.BranchType2 && exact)
            {
                if (command.UserId is null || command.UserId == Guid.Empty || string.IsNullOrWhiteSpace(command.Authorization))
                    throw Rule("OPERATOR_AUTHORIZATION_REQUIRED", "سجل دخول مستخدم لديه صلاحية استلام المخزون.");
                locationTransaction = new BranchInventoryTransaction { Id = Guid.NewGuid(), ReferenceId = receipt.Id, SiteId = configuration.SiteId,
                    UserId = command.UserId.Value, Kind = Branch2TransactionKind.IncomingReceipt, Reason = "Kitchen shipment accepted",
                    Fingerprint = command.CommandId.ToString(), OccurredAtUtc = now };
            }
            QuantityConflict? conflict = null;
            if (!exact)
            {
                conflict = new QuantityConflict
                {
                    Id = Guid.NewGuid(),
                    ReceiptId = receipt.Id,
                    Status = "PENDING_ADMIN",
                    ReportedAtUtc = now
                };
            }

            foreach (var shipmentLine in shipment.Lines.OrderBy(value => value.ItemId))
            {
                var counted = counts[shipmentLine.Id];
                var receiptLine = new IncomingReceiptLine
                {
                    Id = Guid.NewGuid(),
                    ReceiptId = receipt.Id,
                    ShipmentLineId = shipmentLine.Id,
                    ItemId = shipmentLine.ItemId,
                    SentScaled = shipmentLine.SentScaled,
                    CountedScaled = counted,
                    ConfirmedScaled = exact ? counted : null
                };
                receipt.Lines.Add(receiptLine);
                if (exact)
                {
                    var snapshot = await GetOrCreateShiftSnapshotAsync(db, shift, shipmentLine.ItemId, cancellationToken);
                    if (locationTransaction is not null)
                    {
                        var balance = await db.LocationBalances.FindAsync([shipmentLine.ItemId, BranchInventoryLocation.Stock], cancellationToken);
                        if (balance is null) { balance = new BranchLocationBalance { ItemId = shipmentLine.ItemId, Location = BranchInventoryLocation.Stock }; db.LocationBalances.Add(balance); }
                        balance.QuantityScaled = checked(balance.QuantityScaled + counted); balance.Version++;
                        locationTransaction.Lines.Add(new BranchInventoryTransactionLine { Id = Guid.NewGuid(), ItemId = shipmentLine.ItemId, Location = BranchInventoryLocation.Stock, DeltaScaled = counted });
                    }
                    else
                    {
                        var balance = await db.StockBalances.SingleAsync(value => value.ItemId == shipmentLine.ItemId, cancellationToken);
                        balance.QuantityScaled += counted; balance.Revision += 1; balance.AsOfUtc = now;
                    }
                    snapshot.IncomingScaled += counted;
                    snapshot.ExpectedCloseScaled += counted;
                    db.StockMovements.Add(new StockMovement
                    {
                        Id = Guid.NewGuid(),
                        DocumentId = receipt.Id,
                        SourceLineId = receiptLine.Id,
                        ItemId = shipmentLine.ItemId,
                        ShiftId = shift.Id,
                        Kind = StockMovementKind.IncomingReceipt,
                        DeltaScaled = counted,
                        OccurredAtUtc = now
                    });
                }
                else
                {
                    db.StockHolds.Add(new StockHold
                    {
                        Id = Guid.NewGuid(),
                        ReceiptLineId = receiptLine.Id,
                        ItemId = shipmentLine.ItemId,
                        PhysicalCountScaled = counted,
                        Status = HoldStatus.PendingDecision
                    });
                    conflict!.Lines.Add(new ConflictLine
                    {
                        Id = Guid.NewGuid(),
                        ConflictId = conflict.Id,
                        ReceiptLineId = receiptLine.Id,
                        SentScaled = shipmentLine.SentScaled,
                        CountedScaled = counted,
                        Note = counted == shipmentLine.SentScaled ? "مطابق؛ محتجز مع كامل الشحنة" : "فرق في العد"
                    });
                }
            }
            shipment.Status = exact ? ShipmentStatus.Received : ShipmentStatus.Disputed;
            shipment.Version += 1;
            db.IncomingReceipts.Add(receipt);
            if (conflict is not null) db.QuantityConflicts.Add(conflict);
            var sequence = await GetSequenceAsync(db, cancellationToken);
            QueueEvent(db, sequence, receipt.Id, exact ? "incoming_receipt.accepted" : "incoming_receipt.disputed", now, new
            {
                receipt_id = receipt.Id,
                conflict_id = conflict?.Id,
                shipment_id = shipment.Id,
                receiving_shift_id = shift.Id,
                shipment_version = command.ExpectedVersion,
                status = exact ? "ACCEPTED" : "DISPUTED",
                entire_shipment_held = !exact,
                lines = receipt.Lines.Select(value => new
                {
                    receipt_line_id = value.Id,
                    shipment_line_id = value.ShipmentLineId,
                    item_id = value.ItemId,
                    sent_scaled = value.SentScaled,
                    counted_scaled = value.CountedScaled,
                    confirmed_scaled = value.ConfirmedScaled
                }).ToArray()
            });
            if (locationTransaction is not null)
            {
                db.InventoryTransactions.Add(locationTransaction);
                QueueEvent(db, sequence, locationTransaction.Id, "branch2.inventory.posted", now, new {
                    transaction_id = locationTransaction.Id, reference_id = receipt.Id, user_id = locationTransaction.UserId, authorization = command.Authorization,
                    kind = "IncomingReceipt", reason = locationTransaction.Reason,
                    lines = locationTransaction.Lines.Select(x => new { item_id = x.ItemId, location = "FREEZER", delta_scaled = x.DeltaScaled.ToString() }).ToArray()
                }, locationTransaction.Id);
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new IncomingReceiptResult(receipt.Id, shipment.Id, receipt.Status, !exact, false);
        }
        finally
        {
            database.WriteLock.Release();
        }
    }

    public async Task<ManualIncomingResult> PostManualIncomingAsync(
        PostManualIncomingCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateCommandId(command.CommandId);
        ValidateQuantityInputs(command.Lines, "أدخل كمية وارد موجبة لصنف واحد على الأقل.");
        var reason = command.Reason.Trim();
        if (reason.Length < 3) throw Rule("REASON_REQUIRED", "اكتب مصدر أو سبب الوارد اليدوي.");

        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            var existing = await db.OutboxMessages.AsNoTracking().SingleOrDefaultAsync(x => x.EventId == command.CommandId, cancellationToken);
            if (existing is not null)
            {
                if (existing.EventType != "manual_incoming.posted") throw IdempotencyReuse();
                return new ManualIncomingResult(command.CommandId, true);
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var shift = await RequireOpenShiftAsync(db, cancellationToken);
            var configuration = await db.DeviceConfigurations.SingleAsync(cancellationToken);
            if (configuration.Profile == DeviceProfile.Kitchen) throw Rule("WRONG_PROFILE", "الوارد اليدوي متاح للفروع فقط.");
            if (configuration.Profile == DeviceProfile.BranchType2) RequireLocationActor(command.UserId, command.Authorization);

            var itemIds = command.Lines.Select(x => x.ItemId).ToArray();
            var items = await db.CatalogItems.Where(x => itemIds.Contains(x.Id) && x.Active).ToDictionaryAsync(x => x.Id, cancellationToken);
            if (items.Count != itemIds.Length) throw Rule("ITEM_NOT_FOUND", "أحد أصناف الوارد غير متاح.");
            var now = DateTimeOffset.UtcNow;
            BranchInventoryTransaction? locationTransaction = null;
            if (configuration.Profile == DeviceProfile.BranchType2)
            {
                locationTransaction = new BranchInventoryTransaction
                {
                    Id = command.CommandId, ReferenceId = command.CommandId, SiteId = configuration.SiteId,
                    UserId = command.UserId!.Value, Kind = Branch2TransactionKind.IncomingReceipt,
                    Reason = reason, Fingerprint = command.CommandId.ToString(), OccurredAtUtc = now
                };
                db.InventoryTransactions.Add(locationTransaction);
            }

            var eventLines = new List<object>();
            foreach (var input in command.Lines.OrderBy(x => x.ItemId))
            {
                var sourceLineId = Guid.NewGuid();
                if (locationTransaction is not null)
                {
                    var balance = await db.LocationBalances.FindAsync([input.ItemId, BranchInventoryLocation.Stock], cancellationToken);
                    if (balance is null) { balance = new BranchLocationBalance { ItemId = input.ItemId, Location = BranchInventoryLocation.Stock }; db.LocationBalances.Add(balance); }
                    balance.QuantityScaled = checked(balance.QuantityScaled + input.QuantityScaled);
                    balance.Version = checked(balance.Version + 1);
                    locationTransaction.Lines.Add(new BranchInventoryTransactionLine { Id = sourceLineId, ItemId = input.ItemId, Location = BranchInventoryLocation.Stock, DeltaScaled = input.QuantityScaled });
                }
                else
                {
                    var balance = await db.StockBalances.SingleAsync(x => x.ItemId == input.ItemId, cancellationToken);
                    balance.QuantityScaled = checked(balance.QuantityScaled + input.QuantityScaled);
                    balance.Revision = checked(balance.Revision + 1);
                    balance.AsOfUtc = now;
                }
                var snapshot = await GetOrCreateShiftSnapshotAsync(db, shift, input.ItemId, cancellationToken);
                snapshot.IncomingScaled = checked(snapshot.IncomingScaled + input.QuantityScaled);
                snapshot.ExpectedCloseScaled = checked(snapshot.ExpectedCloseScaled + input.QuantityScaled);
                db.StockMovements.Add(new StockMovement { Id = Guid.NewGuid(), DocumentId = command.CommandId, SourceLineId = sourceLineId,
                    ItemId = input.ItemId, ShiftId = shift.Id, Kind = StockMovementKind.IncomingReceipt, DeltaScaled = input.QuantityScaled, OccurredAtUtc = now });
                eventLines.Add(new { item_id = input.ItemId, quantity_scaled = input.QuantityScaled.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            }
            var sequence = await GetSequenceAsync(db, cancellationToken);
            QueueEvent(db, sequence, command.CommandId, "manual_incoming.posted", now, new {
                document_id = command.CommandId, site_id = configuration.SiteId, shift_id = shift.Id, reason,
                location = configuration.Profile == DeviceProfile.BranchType2 ? "FREEZER" : "SALEABLE", lines = eventLines
            }, command.CommandId);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ManualIncomingResult(command.CommandId, false);
        }
        finally { database.WriteLock.Release(); }
    }

    public async Task<IReadOnlyList<KitchenReturnSnapshot>> GetKitchenReturnsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = database.CreateContext();
        var returns = await db.KitchenReturns.AsNoTracking()
            .Include(value => value.Lines)
            .ToListAsync(cancellationToken);
        return returns.OrderByDescending(value => value.DispatchedAtUtc).Select(value => new KitchenReturnSnapshot(
            value.Id,
            value.Reference,
            value.Status,
            value.DispatchedAtUtc,
            value.Lines.Select(line => new QuantityInput(line.ItemId, line.SentScaled)).ToArray())).ToArray();
    }

    public async Task<KitchenReturnSnapshot> DispatchKitchenReturnAsync(
        DispatchKitchenReturnCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateCommandId(command.CommandId);
        ValidateQuantityInputs(command.Lines, "أدخل كمية مرتجع موجبة لصنف واحد على الأقل.");
        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Trim().Length < 3)
            throw Rule("REASON_REQUIRED", "اكتب سبب مرتجع المطبخ.");
        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            var existing = await db.KitchenReturns.AsNoTracking().Include(value => value.Lines)
                .SingleOrDefaultAsync(value => value.CommandId == command.CommandId, cancellationToken);
            if (existing is not null)
            {
                EnsureSameQuantities(command.Lines, existing.Lines.Select(value => new QuantityInput(value.ItemId, value.SentScaled)));
                var original = await db.OutboxMessages.AsNoTracking().SingleAsync(x => x.AggregateId == existing.Id && x.EventType == "kitchen_return.dispatched", cancellationToken);
                using var originalPayload = System.Text.Json.JsonDocument.Parse(original.PayloadJson);
                if (originalPayload.RootElement.GetProperty("reason").GetString() != command.Reason.Trim()) throw IdempotencyReuse();
                var ledger = await db.InventoryTransactions.AsNoTracking().SingleOrDefaultAsync(x => x.ReferenceId == existing.Id && x.Kind == Branch2TransactionKind.KitchenReturn, cancellationToken);
                if (ledger is not null && ledger.UserId != command.UserId) throw IdempotencyReuse();
                return new KitchenReturnSnapshot(
                    existing.Id,
                    existing.Reference,
                    existing.Status,
                    existing.DispatchedAtUtc,
                    existing.Lines.Select(value => new QuantityInput(value.ItemId, value.SentScaled)).ToArray(),
                    true);
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var shift = await RequireOpenShiftAsync(db, cancellationToken);
            var itemIds = command.Lines.Select(value => value.ItemId).ToArray();
            var returnConfiguration = await RequireBranchConfigurationAsync(db, cancellationToken);
            var locationReturn = returnConfiguration.Profile == DeviceProfile.BranchType2;
            if (locationReturn) RequireLocationActor(command.UserId, command.Authorization);
            var returnBalances = locationReturn ? await db.LocationBalances.Where(x => itemIds.Contains(x.ItemId) && x.Location == BranchInventoryLocation.Stock).ToDictionaryAsync(x => x.ItemId, cancellationToken) : null;
            var items = await db.CatalogItems.Include(value => value.StockBalance)
                .Where(value => itemIds.Contains(value.Id))
                .ToDictionaryAsync(value => value.Id, cancellationToken);
            if (items.Count != itemIds.Length) throw Rule("ITEM_UNAVAILABLE", "أحد أصناف المرتجع غير موجود.");
            foreach (var input in command.Lines.OrderBy(value => value.ItemId))
            {
                var item = items[input.ItemId];
                if (locationReturn ? !returnBalances!.TryGetValue(item.Id, out var available) || available.QuantityScaled < input.QuantityScaled : item.StockBalance is null || item.StockBalance.QuantityScaled < input.QuantityScaled)
                    throw Rule("INSUFFICIENT_STOCK", $"الكمية المتاحة من {item.NameAr} لا تكفي لمرتجع المطبخ.");
            }

            var now = DateTimeOffset.UtcNow;
            var sequenceState = await GetSequenceAsync(db, cancellationToken);
            var kitchenReturn = new KitchenReturn
            {
                Id = Guid.NewGuid(),
                CommandId = command.CommandId,
                ShiftId = shift.Id,
                Reference = $"{shift.BusinessDate.Replace("-", string.Empty, StringComparison.Ordinal)}-KR-{sequenceState.NextReceiptSequence:D4}",
                Status = KitchenReturnStatus.Dispatched,
                DispatchedAtUtc = now
            };
            sequenceState.NextReceiptSequence += 1;
            foreach (var input in command.Lines.OrderBy(value => value.ItemId))
            {
                var item = items[input.ItemId];
                var line = new KitchenReturnLine
                {
                    Id = Guid.NewGuid(),
                    ReturnId = kitchenReturn.Id,
                    ItemId = item.Id,
                    NameSnapshot = item.NameAr,
                    UnitSnapshot = item.Unit,
                    QuantityScale = item.QuantityScale,
                    SentScaled = input.QuantityScaled
                };
                kitchenReturn.Lines.Add(line);
                var snapshot = await GetOrCreateShiftSnapshotAsync(db, shift, item.Id, cancellationToken);
                if (!locationReturn)
                {
                    item.StockBalance!.QuantityScaled -= input.QuantityScaled;
                    item.StockBalance.Revision += 1;
                    item.StockBalance.AsOfUtc = now;
                }
                snapshot.KitchenReturnScaled += input.QuantityScaled;
                snapshot.ExpectedCloseScaled -= input.QuantityScaled;
                db.StockMovements.Add(new StockMovement
                {
                    Id = Guid.NewGuid(),
                    DocumentId = kitchenReturn.Id,
                    SourceLineId = line.Id,
                    ItemId = item.Id,
                    ShiftId = shift.Id,
                    Kind = StockMovementKind.KitchenReturn,
                    DeltaScaled = -input.QuantityScaled,
                    OccurredAtUtc = now
                });
            }
            db.KitchenReturns.Add(kitchenReturn);
            QueueEvent(db, sequenceState, kitchenReturn.Id, "kitchen_return.dispatched", now, new
            {
                return_id = kitchenReturn.Id,
                shift_id = shift.Id,
                reference = kitchenReturn.Reference,
                reason = command.Reason.Trim(),
                lines = kitchenReturn.Lines.Select(value => new
                {
                    line_id = value.Id,
                    item_id = value.ItemId,
                    sent_scaled = value.SentScaled,
                    quantity_scale = value.QuantityScale,
                    name_snapshot = value.NameSnapshot,
                    unit_snapshot = value.UnitSnapshot
                }).ToArray()
            });
            if (locationReturn) QueueStockIssue(db, sequenceState, returnConfiguration, kitchenReturn.Id, command.UserId!.Value, command.Authorization!, Branch2TransactionKind.KitchenReturn, command.Reason.Trim(), command.Lines, returnBalances!, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new KitchenReturnSnapshot(
                kitchenReturn.Id,
                kitchenReturn.Reference,
                kitchenReturn.Status,
                kitchenReturn.DispatchedAtUtc,
                command.Lines);
        }
        finally
        {
            database.WriteLock.Release();
        }
    }

    public async Task<IReadOnlyList<SaleListItemSnapshot>> GetCurrentShiftSalesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = database.CreateContext();
        var shift = await db.Shifts.AsNoTracking().SingleOrDefaultAsync(value => value.Status == ShiftStatus.Open, cancellationToken);
        if (shift is null) return [];
        var sales = await db.Sales.AsNoTracking()
            .Where(value => value.ShiftId == shift.Id)
            .ToListAsync(cancellationToken);
        var saleIds = sales.Select(value => value.Id).ToArray();
        var corrections = await db.SaleCorrections.AsNoTracking()
            .Where(value => saleIds.Contains(value.OriginalSaleId))
            .ToListAsync(cancellationToken);
        var refunded = corrections.GroupBy(value => value.OriginalSaleId).ToDictionary(value => value.Key, value => value.Sum(row => row.RefundTotalMinor));
        return sales.OrderByDescending(value => value.OccurredAtUtc).Select(value =>
        {
            var refund = refunded.GetValueOrDefault(value.Id);
            return new SaleListItemSnapshot(
                value.Id,
                value.ShiftId,
                value.ReceiptNumber,
                value.OccurredAtUtc,
                value.PaymentMethod,
                value.Fulfillment,
                value.TotalMinor,
                refund,
                value.TotalMinor - refund);
        }).ToArray();
    }

    public async Task<SaleDetailsSnapshot> GetSaleAsync(Guid saleId, CancellationToken cancellationToken = default)
    {
        if (saleId == Guid.Empty) throw Rule("SALE_NOT_FOUND", "اختر إيصالاً أولاً.");
        await using var db = database.CreateContext();
        var sale = await db.Sales.AsNoTracking().Include(value => value.Shift).Include(value => value.Lines)
            .SingleOrDefaultAsync(value => value.Id == saleId, cancellationToken)
            ?? throw Rule("SALE_NOT_FOUND", "لم يتم العثور على الإيصال.");
        var corrections = await db.SaleCorrections.AsNoTracking().Include(value => value.Lines)
            .Where(value => value.OriginalSaleId == saleId)
            .ToListAsync(cancellationToken);
        var correctionLines = corrections.SelectMany(value => value.Lines).ToArray();
        var lineDetails = sale.Lines.OrderBy(value => value.NameSnapshot).Select(line =>
        {
            var refundedQuantity = -correctionLines
                .Where(value => value.OriginalSaleLineId == line.Id)
                .Sum(value => value.QuantityDeltaScaled);
            return new SaleLineDetails(
                line.Id,
                line.ItemId,
                line.NameSnapshot,
                line.SkuSnapshot,
                line.UnitSnapshot,
                line.QuantityScale,
                line.QuantityScaled,
                refundedQuantity,
                line.QuantityScaled - refundedQuantity,
                line.UnitPriceMinor,
                line.AllocatedDiscountMinor,
                line.TotalMinor + correctionLines.Where(value => value.OriginalSaleLineId == line.Id).Sum(value => value.AmountDeltaMinor));
        }).ToArray();
        var refund = corrections.Sum(value => value.RefundTotalMinor);
        return new SaleDetailsSnapshot(
            sale.Id,
            sale.ShiftId,
            sale.Shift.Kind,
            sale.ReceiptNumber,
            sale.OccurredAtUtc,
            sale.PaymentMethod,
            sale.Fulfillment,
            sale.SubtotalMinor,
            sale.DiscountMinor,
            sale.TotalMinor,
            refund,
            sale.TotalMinor - refund,
            sale.TipMinor,
            lineDetails);
    }

    public async Task<SaleCorrectionResult> CorrectSaleAsync(
        CorrectSaleCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateCommandId(command.CommandId);
        if (command.SaleId == Guid.Empty || command.SaleLineId == Guid.Empty || command.QuantityScaled <= 0)
            throw Rule("INVALID_CORRECTION", "اختر الصنف وأدخل كمية استرجاع موجبة.");
        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Trim().Length < 3)
            throw Rule("REASON_REQUIRED", "اكتب سبب الاسترجاع أو التصحيح.");
        if (string.IsNullOrWhiteSpace(command.Actor))
            throw Rule("ACTOR_REQUIRED", "يلزم اسم المستخدم المنفذ للتصحيح.");

        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            var existing = await db.SaleCorrections.AsNoTracking().Include(value => value.Lines)
                .SingleOrDefaultAsync(value => value.CommandId == command.CommandId, cancellationToken);
            if (existing is not null)
            {
                var line = existing.Lines.Single();
                var same = existing.OriginalSaleId == command.SaleId
                    && line.OriginalSaleLineId == command.SaleLineId
                    && -line.QuantityDeltaScaled == command.QuantityScaled
                    && (line.RestockScaled > 0) == command.Restock
                    && string.Equals(existing.Reason, command.Reason.Trim(), StringComparison.Ordinal)
                    && existing.RefundMethod == command.RefundMethod;
                if (!same) throw IdempotencyReuse();
                return new SaleCorrectionResult(existing.Id, existing.RefundTotalMinor, line.RestockScaled > 0, true);
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var postingShift = await RequireOpenShiftAsync(db, cancellationToken);
            var sale = await db.Sales.Include(value => value.Lines)
                .SingleOrDefaultAsync(value => value.Id == command.SaleId, cancellationToken)
                ?? throw Rule("SALE_NOT_FOUND", "لم يتم العثور على الإيصال.");
            var originalLine = sale.Lines.SingleOrDefault(value => value.Id == command.SaleLineId)
                ?? throw Rule("SALE_LINE_NOT_FOUND", "صنف الإيصال لم يعد متاحاً للتصحيح.");
            var previous = await db.CorrectionLines.AsNoTracking()
                .Where(value => value.OriginalSaleLineId == originalLine.Id)
                .ToListAsync(cancellationToken);
            var alreadyRefunded = -previous.Sum(value => value.QuantityDeltaScaled);
            var remaining = originalLine.QuantityScaled - alreadyRefunded;
            if (command.QuantityScaled > remaining)
                throw Rule("REFUND_EXCEEDS_REMAINING", "الكمية المطلوبة أكبر من الكمية المتبقية القابلة للاسترجاع في الإيصال.");

            var targetRefunded = alreadyRefunded + command.QuantityScaled;
            var priorRefundMinor = -previous.Sum(value => value.AmountDeltaMinor);
            var targetRefundMinor = RoundMinor((decimal)originalLine.TotalMinor * targetRefunded / originalLine.QuantityScaled);
            var refundMinor = targetRefundMinor - priorRefundMinor;
            if (refundMinor <= 0)
                throw Rule("INVALID_REFUND", "تعذر حساب قيمة الاسترجاع من سعر وخصم الإيصال الأصلي.");

            var now = DateTimeOffset.UtcNow;
            var correction = new SaleCorrection
            {
                Id = Guid.NewGuid(),
                CommandId = command.CommandId,
                OriginalSaleId = sale.Id,
                PostingShiftId = postingShift.Id,
                Kind = targetRefunded == originalLine.QuantityScaled && sale.Lines.Count == 1 ? CorrectionKind.Void : CorrectionKind.Refund,
                Reason = command.Reason.Trim(),
                Actor = command.Actor.Trim(),
                RefundTotalMinor = refundMinor,
                RefundMethod = command.RefundMethod,
                OccurredAtUtc = now
            };
            var correctionLine = new CorrectionLine
            {
                Id = Guid.NewGuid(),
                CorrectionId = correction.Id,
                OriginalSaleLineId = originalLine.Id,
                ItemId = originalLine.ItemId,
                QuantityDeltaScaled = -command.QuantityScaled,
                AmountDeltaMinor = -refundMinor,
                RestockScaled = command.Restock ? command.QuantityScaled : 0,
                Disposition = command.Restock ? StockDisposition.Restock : StockDisposition.Discard,
                NameSnapshot = originalLine.NameSnapshot,
                UnitSnapshot = originalLine.UnitSnapshot,
                QuantityScale = originalLine.QuantityScale
            };
            correction.Lines.Add(correctionLine);
            correction.Payment = new RefundPayment
            {
                Id = Guid.NewGuid(),
                CorrectionId = correction.Id,
                Method = command.RefundMethod,
                AmountMinor = refundMinor,
                PaidAtUtc = now
            };
            if (command.Restock)
            {
                var balance = await db.StockBalances.SingleAsync(value => value.ItemId == originalLine.ItemId, cancellationToken);
                balance.QuantityScaled += command.QuantityScaled;
                balance.Revision += 1;
                balance.AsOfUtc = now;
                var snapshot = await GetOrCreateShiftSnapshotAsync(db, postingShift, originalLine.ItemId, cancellationToken);
                snapshot.CustomerRestockScaled += command.QuantityScaled;
                snapshot.ExpectedCloseScaled += command.QuantityScaled;
                db.StockMovements.Add(new StockMovement
                {
                    Id = Guid.NewGuid(),
                    DocumentId = correction.Id,
                    SourceLineId = correctionLine.Id,
                    ItemId = originalLine.ItemId,
                    ShiftId = postingShift.Id,
                    Kind = StockMovementKind.CustomerRestock,
                    DeltaScaled = command.QuantityScaled,
                    OccurredAtUtc = now
                });
            }
            db.CashMovements.Add(new CashMovement
            {
                Id = Guid.NewGuid(),
                ShiftId = postingShift.Id,
                SourceId = correction.Id,
                Kind = command.RefundMethod == PaymentMethod.Cash ? "REFUND_CASH" : "REFUND_VISA",
                AmountMinor = -refundMinor,
                OccurredAtUtc = now
            });
            db.SaleCorrections.Add(correction);
            db.SideEffectJobs.Add(new SideEffectJob
            {
                Id = Guid.NewGuid(),
                SourceId = correction.Id,
                Kind = SideEffectKind.PrintReceipt,
                State = SideEffectState.Pending,
                CreatedAtUtc = now,
                NextAttemptAtUtc = now
            });
            var sequence = await GetSequenceAsync(db, cancellationToken);
            QueueEvent(db, sequence, correction.Id, "sale.corrected", now, new
            {
                correction_id = correction.Id,
                original_sale_id = sale.Id,
                posting_shift_id = postingShift.Id,
                reason = correction.Reason,
                actor = correction.Actor,
                refund_method = command.RefundMethod == PaymentMethod.Cash ? "CASH" : "VISA",
                refund_minor = refundMinor,
                original_tip_unchanged_minor = sale.TipMinor,
                lines = new[]
                {
                    new
                    {
                        correction_line_id = correctionLine.Id,
                        original_sale_line_id = originalLine.Id,
                        item_id = originalLine.ItemId,
                        quantity_delta_scaled = correctionLine.QuantityDeltaScaled,
                        amount_delta_minor = correctionLine.AmountDeltaMinor,
                        restock_scaled = correctionLine.RestockScaled,
                        disposition = correctionLine.Disposition == StockDisposition.Restock ? "RESTOCK" : "DISCARD",
                        quantity_scale = correctionLine.QuantityScale
                    }
                }
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new SaleCorrectionResult(correction.Id, refundMinor, command.Restock, false);
        }
        finally
        {
            database.WriteLock.Release();
        }
    }

    public async Task<IReadOnlyList<CafeProfileSnapshot>> GetCafeProfilesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = database.CreateContext();
        var customers = await db.CafeCustomers.AsNoTracking().Where(value => value.Active).OrderBy(value => value.Name).ToListAsync(cancellationToken);
        var orders = await db.CustomOrders.AsNoTracking().Include(value => value.Payments).ToListAsync(cancellationToken);
        return customers.Select(customer => ToCafeProfileSnapshot(customer, orders)).ToArray();
    }

    public async Task<CafeProfileDetailsSnapshot> GetCafeProfileAsync(Guid cafeCustomerId, CancellationToken cancellationToken = default)
    {
        await using var db = database.CreateContext();
        var customer = await db.CafeCustomers.AsNoTracking().SingleOrDefaultAsync(value => value.Id == cafeCustomerId, cancellationToken)
            ?? throw Rule("CAFE_NOT_FOUND", "لم يتم العثور على العميل.");
        var prices = await db.CafePrices.AsNoTracking().Include(value => value.Item)
            .Where(value => value.CafeCustomerId == cafeCustomerId)
            .OrderBy(value => value.Item.NameAr)
            .Select(value => new CafePriceSnapshot(value.ItemId, value.Item.Sku, value.Item.NameAr, value.Item.Unit, value.Item.QuantityScale, value.UnitPriceMinor))
            .ToListAsync(cancellationToken);
        var orders = await db.CustomOrders.AsNoTracking().Include(value => value.Payments).Include(value => value.Lines)
            .Where(value => value.CafeCustomerId == cafeCustomerId)
            .ToListAsync(cancellationToken);
        var snapshots = orders.OrderByDescending(value => value.CreatedAtUtc)
            .Select(value => ToCustomOrderSnapshot(value, orders, false)).ToArray();
        var payments = orders.SelectMany(order => order.Payments.Select(payment => new CafePaymentSnapshot(
                payment.Id, order.OrderNumber, payment.AmountMinor, payment.Method, payment.PaidAtUtc)))
            .OrderByDescending(value => value.PaidAtUtc).ToArray();
        return new CafeProfileDetailsSnapshot(ToCafeProfileSnapshot(customer, orders), prices, snapshots, payments);
    }

    public async Task<CafeProfileSnapshot> CreateCafeProfileAsync(CreateCafeProfileCommand command, CancellationToken cancellationToken = default)
    {
        ValidateCommandId(command.CommandId);
        var name = command.Name.Trim();
        var kind = command.Kind.Trim();
        var phone = NormalizeCustomerPhone(command.Phone);
        var address = command.Address.Trim();
        if (name.Length < 2 || phone.Length < 5 || kind.Length < 2)
            throw Rule("CAFE_FIELDS_REQUIRED", "اكتب اسم العميل ونوعه ورقم الهاتف.");

        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            var replay = await db.CafeCustomers.AsNoTracking().SingleOrDefaultAsync(value => value.CommandId == command.CommandId, cancellationToken);
            if (replay is not null)
            {
                if (replay.Name != name || replay.Kind != kind || replay.Phone != phone || replay.Address != address) throw IdempotencyReuse();
                return ToCafeProfileSnapshot(replay, []);
            }
            if (await db.CafeCustomers.AnyAsync(value => value.Phone == phone, cancellationToken))
                throw Rule("CAFE_PHONE_EXISTS", "يوجد عميل محفوظ بالفعل بنفس رقم الهاتف.");

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var configuration = await RequireBranchConfigurationAsync(db, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var customer = new CafeCustomer
            {
                Id = Guid.NewGuid(), CommandId = command.CommandId, Name = name, Kind = kind, Phone = phone, Address = address,
                Active = true, Version = 1, CreatedAtUtc = now, UpdatedAtUtc = now
            };
            var catalog = await db.CatalogItems.AsNoTracking().Where(value => value.Active).ToListAsync(cancellationToken);
            Dictionary<Guid, long>? copiedPrices = null;
            if (command.CopyPricesFromCafeId is Guid sourceId)
            {
                if (!await db.CafeCustomers.AnyAsync(value => value.Id == sourceId && value.Active, cancellationToken))
                    throw Rule("COPY_CAFE_NOT_FOUND", "قائمة الأسعار المطلوب نسخها غير موجودة.");
                copiedPrices = await db.CafePrices.AsNoTracking().Where(value => value.CafeCustomerId == sourceId)
                    .ToDictionaryAsync(value => value.ItemId, value => value.UnitPriceMinor, cancellationToken);
            }
            foreach (var item in catalog)
                customer.Prices.Add(new CafePrice
                {
                    Id = Guid.NewGuid(), CafeCustomerId = customer.Id, ItemId = item.Id,
                    UnitPriceMinor = copiedPrices?.GetValueOrDefault(item.Id, item.RetailPriceMinor) ?? item.RetailPriceMinor,
                    UpdatedAtUtc = now
                });
            db.CafeCustomers.Add(customer);
            var sequence = await GetSequenceAsync(db, cancellationToken);
            QueueEvent(db, sequence, customer.Id, "cafe_customer.created", now, new
            {
                customer_id = customer.Id, site_id = configuration.SiteId, name = customer.Name, kind = customer.Kind,
                phone = customer.Phone, address = customer.Address, copied_from_customer_id = command.CopyPricesFromCafeId,
                prices = customer.Prices.Select(value => new { item_id = value.ItemId, unit_price_minor = value.UnitPriceMinor }).ToArray(),
                version = customer.Version
            }, command.CommandId);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToCafeProfileSnapshot(customer, []);
        }
        finally { database.WriteLock.Release(); }
    }

    public async Task<CafeProfileDetailsSnapshot> SaveCafePriceListAsync(SaveCafePriceListCommand command, CancellationToken cancellationToken = default)
    {
        ValidateCommandId(command.CommandId);
        if (command.CafeCustomerId == Guid.Empty || command.ExpectedVersion < 1 || command.Prices.Count == 0
            || command.Prices.Any(value => value.ItemId == Guid.Empty || value.UnitPriceMinor < 0)
            || command.Prices.Select(value => value.ItemId).Distinct().Count() != command.Prices.Count)
            throw Rule("INVALID_CAFE_PRICES", "راجع قائمة الأسعار.");

        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            var customer = await db.CafeCustomers.Include(value => value.Prices)
                .SingleOrDefaultAsync(value => value.Id == command.CafeCustomerId, cancellationToken)
                ?? throw Rule("CAFE_NOT_FOUND", "لم يتم العثور على العميل.");
            if (customer.Version != command.ExpectedVersion)
                throw Rule("STALE_VERSION", "تم تحديث قائمة الأسعار. افتح العميل من جديد ثم حاول مرة أخرى.");
            var now = DateTimeOffset.UtcNow;
            foreach (var input in command.Prices)
            {
                var price = customer.Prices.SingleOrDefault(value => value.ItemId == input.ItemId)
                    ?? throw Rule("CAFE_PRICE_ITEM_NOT_FOUND", "أحد الأصناف غير موجود في قائمة العميل.");
                price.UnitPriceMinor = input.UnitPriceMinor;
                price.UpdatedAtUtc = now;
            }
            customer.Version += 1;
            customer.UpdatedAtUtc = now;
            var sequence = await GetSequenceAsync(db, cancellationToken);
            QueueEvent(db, sequence, customer.Id, "cafe_customer.price_list_updated", now, new
            {
                customer_id = customer.Id,
                prices = command.Prices.Select(value => new { item_id = value.ItemId, unit_price_minor = value.UnitPriceMinor }).ToArray(),
                version = customer.Version
            }, command.CommandId);
            await db.SaveChangesAsync(cancellationToken);
        }
        finally { database.WriteLock.Release(); }
        return await GetCafeProfileAsync(command.CafeCustomerId, cancellationToken);
    }

    public async Task ArchiveCafeProfileAsync(Guid commandId, Guid cafeCustomerId, int expectedVersion, CancellationToken cancellationToken = default)
    {
        ValidateCommandId(commandId);
        if (cafeCustomerId == Guid.Empty || expectedVersion < 1)
            throw Rule("INVALID_CAFE", "اختر الكافيه المطلوب حذفه.");
        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            var replay = await db.OutboxMessages.AsNoTracking().SingleOrDefaultAsync(x => x.EventId == commandId, cancellationToken);
            if (replay is not null)
            {
                if (replay.AggregateId != cafeCustomerId || replay.EventType != "cafe_customer.archived") throw IdempotencyReuse();
                return;
            }
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var configuration = await RequireBranchConfigurationAsync(db, cancellationToken);
            if (configuration.Profile != DeviceProfile.BranchType2)
                throw Rule("WRONG_PROFILE", "إدارة حسابات الكافيهات متاحة لفرع نوع ٢ فقط.");
            var customer = await db.CafeCustomers.SingleOrDefaultAsync(x => x.Id == cafeCustomerId, cancellationToken)
                ?? throw Rule("CAFE_NOT_FOUND", "لم يتم العثور على الكافيه.");
            if (customer.Version != expectedVersion) throw Rule("STALE_VERSION", "تم تحديث الكافيه. أعد فتحه ثم حاول مرة أخرى.");
            customer.Active = false;
            customer.Version += 1;
            customer.UpdatedAtUtc = DateTimeOffset.UtcNow;
            var sequence = await GetSequenceAsync(db, cancellationToken);
            QueueEvent(db, sequence, customer.Id, "cafe_customer.archived", customer.UpdatedAtUtc, new
            {
                customer_id = customer.Id,
                site_id = configuration.SiteId,
                version = customer.Version
            }, commandId);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally { database.WriteLock.Release(); }
    }

    public async Task<IReadOnlyList<CustomOrderSnapshot>> GetCustomOrdersAsync(CancellationToken cancellationToken = default)
    {
        await using var db = database.CreateContext();
        var orders = await db.CustomOrders.AsNoTracking()
            .Include(value => value.Payments)
            .Include(value => value.Lines)
            .ToListAsync(cancellationToken);
        var ordered = orders
            .OrderBy(value => value.Status == CustomOrderStatus.Delivered || value.Status == CustomOrderStatus.Cancelled)
            .ThenBy(value => value.DueAtUtc)
            .ToArray();
        return ordered.Select(value => ToCustomOrderSnapshot(value, orders, false)).ToArray();
    }

    public async Task<CustomOrderSnapshot> CreateCustomOrderAsync(
        CreateCustomOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateCommandId(command.CommandId);
        var description = command.Description.Trim();
        if (command.CafeCustomerId == Guid.Empty || command.Lines.Count == 0
            || command.Lines.Any(value => value.ItemId == Guid.Empty || value.QuantityScaled <= 0)
            || command.Lines.Select(value => value.ItemId).Distinct().Count() != command.Lines.Count)
            throw Rule("INVALID_CUSTOM_ORDER_LINES", "اختر صنفاً واحداً على الأقل وحدد الكمية.");
        if (command.DueAtUtc <= DateTimeOffset.UtcNow)
            throw Rule("INVALID_CUSTOM_ORDER_DUE", "موعد الاستلام يجب أن يكون في المستقبل.");

        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            var replay = await db.CustomOrders.AsNoTracking().Include(value => value.Lines)
                .SingleOrDefaultAsync(value => value.CommandId == command.CommandId, cancellationToken);
            if (replay is not null)
            {
                var same = replay.CafeCustomerId == command.CafeCustomerId
                    && replay.Description == description
                    && replay.DueAtUtc == command.DueAtUtc
                    && replay.Lines.OrderBy(value => value.ItemId).Select(value => (value.ItemId, value.QuantityScaled))
                        .SequenceEqual(command.Lines.OrderBy(value => value.ItemId).Select(value => (value.ItemId, value.QuantityScaled)));
                if (!same) throw IdempotencyReuse();
                return await LoadCustomOrderSnapshotAsync(db, replay.Id, true, cancellationToken);
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var configuration = await RequireBranchConfigurationAsync(db, cancellationToken);
            var customer = await db.CafeCustomers.AsNoTracking().SingleOrDefaultAsync(
                value => value.Id == command.CafeCustomerId && value.Active, cancellationToken)
                ?? throw Rule("CAFE_NOT_FOUND", "لم يتم العثور على العميل.");
            var itemIds = command.Lines.Select(value => value.ItemId).ToArray();
            var prices = await db.CafePrices.AsNoTracking().Include(value => value.Item)
                .Where(value => value.CafeCustomerId == customer.Id && itemIds.Contains(value.ItemId))
                .ToDictionaryAsync(value => value.ItemId, cancellationToken);
            if (prices.Count != itemIds.Length)
                throw Rule("CAFE_PRICE_MISSING", "أحد الأصناف ليس له سعر في قائمة العميل.");
            var now = DateTimeOffset.UtcNow;
            var sequence = await GetSequenceAsync(db, cancellationToken);
            var order = new CustomOrder
            {
                Id = Guid.NewGuid(),
                CommandId = command.CommandId,
                CafeCustomerId = customer.Id,
                OrderNumber = $"SP-{CairoBusinessDate(now).Replace("-", string.Empty, StringComparison.Ordinal)}-{sequence.NextCustomOrderSequence:D4}",
                CustomerName = customer.Name,
                CustomerPhone = customer.Phone,
                Description = description,
                DueAtUtc = command.DueAtUtc,
                Status = CustomOrderStatus.New,
                Version = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            foreach (var input in command.Lines)
            {
                var price = prices[input.ItemId];
                var total = RoundMinor((decimal)price.UnitPriceMinor * input.QuantityScaled / price.Item.QuantityScale);
                order.Lines.Add(new CustomOrderLine
                {
                    Id = Guid.NewGuid(), CustomOrderId = order.Id, ItemId = price.ItemId,
                    ItemNameSnapshot = price.Item.NameAr, UnitSnapshot = price.Item.Unit,
                    QuantityScale = price.Item.QuantityScale, QuantityScaled = input.QuantityScaled,
                    UnitPriceMinor = price.UnitPriceMinor, LineTotalMinor = total
                });
            }
            order.TotalMinor = order.Lines.Sum(value => value.LineTotalMinor);
            sequence.NextCustomOrderSequence += 1;
            order.Activities.Add(new CustomOrderActivity
            {
                Id = Guid.NewGuid(),
                CommandId = command.CommandId,
                CustomOrderId = order.Id,
                Kind = "CREATED",
                Note = "تم إنشاء الطلب",
                OccurredAtUtc = now
            });
            db.CustomOrders.Add(order);
            QueueEvent(db, sequence, order.Id, "custom_order.created", now, new
            {
                custom_order_id = order.Id,
                customer_id = customer.Id,
                site_id = configuration.SiteId,
                order_number = order.OrderNumber,
                customer_name = order.CustomerName,
                customer_phone = order.CustomerPhone,
                description = order.Description,
                due_at_utc = order.DueAtUtc,
                total_minor = order.TotalMinor,
                customer_account_key = order.CustomerPhone,
                customer_price_version = customer.Version,
                lines = order.Lines.Select(value => new
                {
                    line_id = value.Id, item_id = value.ItemId, item_name = value.ItemNameSnapshot,
                    unit = value.UnitSnapshot, quantity_scale = value.QuantityScale, quantity_scaled = value.QuantityScaled,
                    unit_price_minor = value.UnitPriceMinor, line_total_minor = value.LineTotalMinor
                }).ToArray(),
                status = "NEW",
                version = order.Version
            }, command.CommandId);
            await db.SaveChangesAsync(cancellationToken);
            var result = await LoadCustomOrderSnapshotAsync(db, order.Id, false, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        finally
        {
            database.WriteLock.Release();
        }
    }

    public async Task<CustomOrderSnapshot> AddCustomOrderPaymentAsync(
        AddCustomOrderPaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateCommandId(command.CommandId);
        if (command.CustomOrderId == Guid.Empty || command.ExpectedVersion < 1 || command.AmountMinor <= 0)
            throw Rule("INVALID_CUSTOM_ORDER_PAYMENT", "أدخل مبلغاً صحيحاً للطلب المحدد.");

        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            var replayPayment = await db.CustomOrderPayments.AsNoTracking()
                .SingleOrDefaultAsync(value => value.CommandId == command.CommandId, cancellationToken);
            if (replayPayment is not null)
            {
                if (replayPayment.CustomOrderId != command.CustomOrderId
                    || replayPayment.AmountMinor != command.AmountMinor
                    || replayPayment.Method != command.PaymentMethod)
                    throw IdempotencyReuse();
                return await LoadCustomOrderSnapshotAsync(db, command.CustomOrderId, true, cancellationToken);
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var configuration = await RequireBranchConfigurationAsync(db, cancellationToken);
            var order = await db.CustomOrders.Include(value => value.Lines)
                .SingleOrDefaultAsync(value => value.Id == command.CustomOrderId, cancellationToken)
                ?? throw Rule("CUSTOM_ORDER_NOT_FOUND", "لم يتم العثور على الطلب الخاص.");
            if (order.Version != command.ExpectedVersion)
                throw Rule("STALE_VERSION", "تم تحديث الطلب. افتحه من جديد ثم حاول مرة أخرى.");
            var allOrders = await db.CustomOrders.Include(value => value.Payments).ToListAsync(cancellationToken);
            var customerOrders = allOrders
                .Where(value => order.CafeCustomerId is Guid customerId
                    ? value.CafeCustomerId == customerId
                    : NormalizeCustomerPhone(value.CustomerPhone) == NormalizeCustomerPhone(order.CustomerPhone))
                .ToArray();
            var accountBalance = customerOrders.Where(value => value.Status != CustomOrderStatus.Cancelled).Sum(value => value.TotalMinor)
                - customerOrders.SelectMany(value => value.Payments).Sum(value => value.AmountMinor);
            if (command.AmountMinor > accountBalance)
                throw Rule("PAYMENT_EXCEEDS_BALANCE", "المبلغ أكبر من رصيد العميل المستحق.");

            var now = DateTimeOffset.UtcNow;
            var autoConfirmed = order.Status == CustomOrderStatus.New;
            if (autoConfirmed) order.Status = CustomOrderStatus.Confirmed;
            var payment = new CustomOrderPayment
            {
                Id = Guid.NewGuid(),
                CommandId = command.CommandId,
                CustomOrderId = order.Id,
                CafeCustomerId = order.CafeCustomerId,
                ShiftId = null,
                Method = command.PaymentMethod,
                AmountMinor = command.AmountMinor,
                PaidAtUtc = now
            };
            payment.Order = order;
            db.CustomOrderPayments.Add(payment);
            order.Version += 1;
            order.UpdatedAtUtc = now;
            db.CustomOrderActivities.Add(new CustomOrderActivity
            {
                Id = Guid.NewGuid(),
                CommandId = command.CommandId,
                CustomOrderId = order.Id,
                Kind = "PAYMENT_ADDED",
                Note = $"تم تسجيل دفعة {command.AmountMinor}",
                OccurredAtUtc = now,
                Order = order
            });
            var printJob = NewJob(order.Id, SideEffectKind.PrintCustomOrder, now);
            printJob.DocumentVersion = order.Version;
            db.SideEffectJobs.Add(printJob);
            var sequence = await GetSequenceAsync(db, cancellationToken);
            if (autoConfirmed)
                QueueEvent(db, sequence, order.Id, "custom_order.status_changed", now, new
                {
                    custom_order_id = order.Id,
                    status = "CONFIRMED",
                    stock_lines = Array.Empty<object>(),
                    version = order.Version
                });
            QueueEvent(db, sequence, order.Id, "custom_customer.payment_recorded", now, new
            {
                payment_id = payment.Id,
                source_custom_order_id = order.Id,
                customer_id = order.CafeCustomerId,
                site_id = configuration.SiteId,
                customer_account_key = order.CustomerPhone,
                customer_name = order.CustomerName,
                amount_minor = payment.AmountMinor,
                payment_method = payment.Method == PaymentMethod.Cash ? "CASH" : "VISA",
                version = order.Version
            }, command.CommandId);
            await db.SaveChangesAsync(cancellationToken);
            var result = await LoadCustomOrderSnapshotAsync(db, order.Id, false, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        finally
        {
            database.WriteLock.Release();
        }
    }

    public async Task<CustomOrderSnapshot> ChangeCustomOrderStatusAsync(
        ChangeCustomOrderStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateCommandId(command.CommandId);
        if (command.CustomOrderId == Guid.Empty || command.ExpectedVersion < 1)
            throw Rule("INVALID_CUSTOM_ORDER", "الطلب الخاص المحدد غير صالح.");

        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            var replayActivity = await db.CustomOrderActivities.AsNoTracking()
                .SingleOrDefaultAsync(value => value.CommandId == command.CommandId, cancellationToken);
            if (replayActivity is not null)
            {
                if (replayActivity.CustomOrderId != command.CustomOrderId || replayActivity.Kind != $"STATUS_{command.Status.ToString().ToUpperInvariant()}")
                    throw IdempotencyReuse();
                return await LoadCustomOrderSnapshotAsync(db, command.CustomOrderId, true, cancellationToken);
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var orderConfiguration = await RequireBranchConfigurationAsync(db, cancellationToken);
            var locationOrder = orderConfiguration.Profile == DeviceProfile.BranchType2;
            BranchInventoryTransaction? cafeTransaction = null;
            var order = await db.CustomOrders.Include(value => value.Lines)
                .SingleOrDefaultAsync(value => value.Id == command.CustomOrderId, cancellationToken)
                ?? throw Rule("CUSTOM_ORDER_NOT_FOUND", "لم يتم العثور على الطلب الخاص.");
            if (order.Version != command.ExpectedVersion)
                throw Rule("STALE_VERSION", "تم تحديث الطلب. افتحه من جديد ثم حاول مرة أخرى.");
            var validTransition = (order.Status, command.Status) switch
            {
                (CustomOrderStatus.New, CustomOrderStatus.Delivered) => true,
                (CustomOrderStatus.New, CustomOrderStatus.Cancelled) => true,
                // Historical orders retain their recorded states during upgrade.
                // They can still be delivered/cancelled without an extra step.
                (CustomOrderStatus.Confirmed, CustomOrderStatus.Delivered) => true,
                (CustomOrderStatus.Confirmed, CustomOrderStatus.Cancelled) => true,
                (CustomOrderStatus.Ready, CustomOrderStatus.Delivered) => true,
                (CustomOrderStatus.Ready, CustomOrderStatus.Cancelled) => true,
                _ => false
            };
            if (!validTransition)
                throw Rule("INVALID_CUSTOM_ORDER_STATUS", "هذا الانتقال غير متاح لحالة الطلب الحالية.");
            var now = DateTimeOffset.UtcNow;
            if (command.Status == CustomOrderStatus.Delivered && order.Lines.Count > 0)
            {
                var itemIds = order.Lines.Select(value => value.ItemId).ToArray();
                var balances = await db.StockBalances.Where(value => itemIds.Contains(value.ItemId))
                    .ToDictionaryAsync(value => value.ItemId, cancellationToken);
                var locationBalances = locationOrder ? await db.LocationBalances.Where(x => itemIds.Contains(x.ItemId) && x.Location == BranchInventoryLocation.Stock).ToDictionaryAsync(x => x.ItemId, cancellationToken) : null;
                if (locationOrder) RequireLocationActor(command.UserId, command.Authorization);
                if (locationOrder ? locationBalances!.Count != itemIds.Length || order.Lines.Any(x => locationBalances[x.ItemId].QuantityScaled < x.QuantityScaled) : balances.Count != itemIds.Length || order.Lines.Any(value => balances[value.ItemId].QuantityScaled < value.QuantityScaled))
                    throw Rule("INSUFFICIENT_STOCK", "المخزون لا يكفي لتسليم هذه الفاتورة.");
                var shift = await db.Shifts.Include(value => value.OpeningItems)
                    .SingleOrDefaultAsync(value => value.Status == ShiftStatus.Open, cancellationToken)
                    ?? throw Rule("SHIFT_REQUIRED", "افتح وردية قبل تسليم الطلب وخصم أصنافه من المخزون.");
                foreach (var line in order.Lines)
                {
                    if (!locationOrder)
                    {
                        var balance = balances[line.ItemId];
                        balance.QuantityScaled -= line.QuantityScaled;
                        balance.Revision += 1;
                        balance.AsOfUtc = now;
                    }
                    db.StockMovements.Add(new StockMovement
                    {
                        Id = Guid.NewGuid(), DocumentId = order.Id, SourceLineId = line.Id, ItemId = line.ItemId,
                        ShiftId = shift.Id, Kind = StockMovementKind.CafeIssue,
                        DeltaScaled = -line.QuantityScaled, OccurredAtUtc = now
                    });
                    var snapshot = await GetOrCreateShiftSnapshotAsync(db, shift, line.ItemId, cancellationToken);
                    snapshot.CafeIssuedScaled += line.QuantityScaled;
                    snapshot.ExpectedCloseScaled -= line.QuantityScaled;
                }
                if (locationOrder) cafeTransaction = CreateStockIssue(db, orderConfiguration, order.Id, command.UserId!.Value, Branch2TransactionKind.CafeIssue, "Cafe order delivery", order.Lines.Select(x => new QuantityInput(x.ItemId, x.QuantityScaled)), locationBalances!, now);
            }
            order.Status = command.Status;
            order.Version += 1;
            order.UpdatedAtUtc = now;
            db.CustomOrderActivities.Add(new CustomOrderActivity
            {
                Id = Guid.NewGuid(),
                CommandId = command.CommandId,
                CustomOrderId = order.Id,
                Kind = $"STATUS_{command.Status.ToString().ToUpperInvariant()}",
                Note = CustomOrderStatusText(command.Status),
                OccurredAtUtc = now,
                Order = order
            });
            if (command.Status == CustomOrderStatus.Delivered)
            {
                var printJob = NewJob(order.Id, SideEffectKind.PrintCustomOrder, now);
                printJob.DocumentVersion = order.Version;
                db.SideEffectJobs.Add(printJob);
            }
            var sequence = await GetSequenceAsync(db, cancellationToken);
            QueueEvent(db, sequence, order.Id, "custom_order.status_changed", now, new
            {
                custom_order_id = order.Id,
                status = command.Status.ToString().ToUpperInvariant(),
                stock_lines = command.Status == CustomOrderStatus.Delivered
                    ? order.Lines.Select(value => new { line_id = value.Id, item_id = value.ItemId, quantity_scaled = value.QuantityScaled }).ToArray()
                    : [],
                version = order.Version
            }, command.CommandId);
            if (cafeTransaction is not null) QueueLocationTransaction(db, sequence, cafeTransaction, command.Authorization!);
            await db.SaveChangesAsync(cancellationToken);
            var result = await LoadCustomOrderSnapshotAsync(db, order.Id, false, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        finally
        {
            database.WriteLock.Release();
        }
    }

    public async Task<ClosingPreviewSnapshot> GetClosingPreviewAsync(CancellationToken cancellationToken = default)
    {
        await using var db = database.CreateContext();
        var shift = await db.Shifts.AsNoTracking()
            .Include(value => value.OpeningItems)
            .SingleOrDefaultAsync(value => value.Status == ShiftStatus.Open, cancellationToken)
            ?? throw Rule("SHIFT_REQUIRED", "لا توجد وردية مفتوحة لإغلاقها.");
        return await BuildClosingPreviewAsync(db, shift, cancellationToken);
    }

    public async Task<CloseShiftResult> CloseShiftAsync(
        CloseShiftCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateCommandId(command.CommandId);
        if (command.ActualCashMinor < 0)
            throw Rule("INVALID_ACTUAL_CASH", "النقدية الفعلية لا يمكن أن تكون سالبة.");
        if (command.Counts.Any(value => value.ItemId == Guid.Empty || value.ActualScaled < 0)
            || command.Counts.Select(value => value.ItemId).Distinct().Count() != command.Counts.Count)
            throw Rule("INVALID_STOCK_COUNT", "أدخل العد الفعلي لكل الأصناف مرة واحدة.");

        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            var existingCount = await db.StockCounts.AsNoTracking().Include(value => value.Lines)
                .SingleOrDefaultAsync(value => value.CommandId == command.CommandId, cancellationToken);
            if (existingCount is not null)
            {
                var shift = await db.Shifts.AsNoTracking().SingleAsync(value => value.Id == existingCount.ShiftId, cancellationToken);
                var same = shift.ActualCashMinor == command.ActualCashMinor
                    && existingCount.Lines.Count == command.Counts.Count
                    && existingCount.Lines.OrderBy(value => value.ItemId)
                        .Zip(command.Counts.OrderBy(value => value.ItemId))
                        .All(value => value.First.ItemId == value.Second.ItemId && value.First.ActualScaled == value.Second.ActualScaled);
                if (!same) throw IdempotencyReuse();
                var existingExport = await db.SideEffectJobs.AsNoTracking().SingleAsync(
                    value => value.SourceId == shift.Id && value.Kind == SideEffectKind.ExportShiftReport && value.DocumentVersion == 1,
                    cancellationToken);
                var existingPrint = await db.SideEffectJobs.AsNoTracking().SingleAsync(
                    value => value.SourceId == shift.Id && value.Kind == SideEffectKind.PrintShiftReport && value.DocumentVersion == 1,
                    cancellationToken);
                return new CloseShiftResult(
                    shift.Id,
                    existingExport.Id,
                    existingPrint.Id,
                    await BuildReportAsync(db, shift.Id, cancellationToken),
                    true);
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var openShift = await db.Shifts.Include(value => value.OpeningItems)
                .SingleOrDefaultAsync(value => value.Status == ShiftStatus.Open, cancellationToken)
                ?? throw Rule("SHIFT_REQUIRED", "لا توجد وردية مفتوحة لإغلاقها.");
            if (openShift.OpeningItems.Count != command.Counts.Count
                || openShift.OpeningItems.Select(value => value.ItemId).Order().SequenceEqual(command.Counts.Select(value => value.ItemId).Order()) is false)
                throw Rule("INCOMPLETE_STOCK_COUNT", "يجب عد كل الأصناف الظاهرة، بما فيها الأصناف غير النشطة التي لها رصيد.");

            var configuration = await RequireBranchConfigurationAsync(db, cancellationToken);
            BranchInventoryTransaction? displayReturn = null;
            if (configuration.Profile == DeviceProfile.BranchType2)
            {
                RequireLocationActor(command.UserId, command.Authorization);
                var display = await db.LocationBalances.Where(x => x.Location == BranchInventoryLocation.Display && x.QuantityScaled > 0).OrderBy(x => x.ItemId).ToListAsync(cancellationToken);
                if (display.Count > 0)
                {
                    var stock = await db.LocationBalances.Where(x => x.Location == BranchInventoryLocation.Stock && display.Select(d => d.ItemId).Contains(x.ItemId)).ToDictionaryAsync(x => x.ItemId, cancellationToken);
                    displayReturn = new BranchInventoryTransaction { Id = command.CommandId, ReferenceId = openShift.Id, SiteId = configuration.SiteId,
                        UserId = command.UserId!.Value, Kind = Branch2TransactionKind.DisplayReturnToStock, Reason = "End Day display return",
                        Fingerprint = $"close:{command.CommandId:N}", OccurredAtUtc = DateTimeOffset.UtcNow };
                    foreach (var source in display)
                    {
                        if (!stock.TryGetValue(source.ItemId, out var target)) { target = new BranchLocationBalance { ItemId = source.ItemId, Location = BranchInventoryLocation.Stock }; db.LocationBalances.Add(target); }
                        var quantity = source.QuantityScaled; source.QuantityScaled = 0; source.Version++;
                        target.QuantityScaled = checked(target.QuantityScaled + quantity); target.Version++;
                        displayReturn.Lines.Add(new BranchInventoryTransactionLine { Id = Guid.NewGuid(), ItemId = source.ItemId, Location = BranchInventoryLocation.Display, DeltaScaled = -quantity });
                        displayReturn.Lines.Add(new BranchInventoryTransactionLine { Id = Guid.NewGuid(), ItemId = source.ItemId, Location = BranchInventoryLocation.Stock, DeltaScaled = quantity });
                    }
                    db.InventoryTransactions.Add(displayReturn);
                }
            }

            var preview = await BuildClosingPreviewAsync(db, openShift, cancellationToken);
            var actualByItem = command.Counts.ToDictionary(value => value.ItemId, value => value.ActualScaled);
            var now = DateTimeOffset.UtcNow;
            var count = new StockCount
            {
                Id = Guid.NewGuid(),
                CommandId = command.CommandId,
                ShiftId = openShift.Id,
                Status = StockCountStatus.Recorded,
                CountedAtUtc = now
            };
            foreach (var snapshot in openShift.OpeningItems.OrderBy(value => value.ItemId))
            {
                var actual = actualByItem[snapshot.ItemId];
                var difference = actual - snapshot.ExpectedCloseScaled;
                snapshot.ActualCloseScaled = actual;
                snapshot.DiscrepancyScaled = difference;
                var line = new StockCountLine
                {
                    Id = Guid.NewGuid(),
                    CountId = count.Id,
                    ItemId = snapshot.ItemId,
                    NameSnapshot = snapshot.NameSnapshot,
                    UnitSnapshot = snapshot.UnitSnapshot,
                    QuantityScale = snapshot.QuantityScale,
                    ExpectedScaled = snapshot.ExpectedCloseScaled,
                    ActualScaled = actual,
                    DifferenceScaled = difference
                };
                count.Lines.Add(line);
                if (difference != 0)
                {
                    count.Status = StockCountStatus.AdjustmentRequested;
                    db.AdjustmentRequests.Add(new AdjustmentRequest
                    {
                        Id = Guid.NewGuid(),
                        CountLineId = line.Id,
                        ProposedDeltaScaled = difference,
                        Reason = "فرق عد إغلاق الوردية؛ ينتظر قرار الإدارة ولا يغير الرصيد تلقائياً.",
                        Status = AdjustmentRequestStatus.PendingAdmin,
                        RequestedAtUtc = now
                    });
                }
            }
            openShift.Status = ShiftStatus.Closed;
            openShift.ClosedAtUtc = now;
            openShift.ExpectedCashMinor = preview.ExpectedCashMinor;
            openShift.ActualCashMinor = command.ActualCashMinor;
            db.StockCounts.Add(count);
            var exportJob = NewJob(openShift.Id, SideEffectKind.ExportShiftReport, now);
            var printJob = NewJob(openShift.Id, SideEffectKind.PrintShiftReport, now);
            var uploadJob = NewJob(openShift.Id, SideEffectKind.UploadShiftReport, now);
            db.SideEffectJobs.AddRange(exportJob, printJob, uploadJob);
            var sequence = await GetSequenceAsync(db, cancellationToken);
            if (displayReturn is not null) QueueLocationTransaction(db, sequence, displayReturn, command.Authorization!);
            QueueEvent(db, sequence, openShift.Id, "shift.closed", now, new
            {
                shift_id = openShift.Id,
                business_date = openShift.BusinessDate,
                kind = openShift.Kind == ShiftKind.Morning ? "MORNING" : "EVENING",
                opened_at = openShift.OpenedAtUtc,
                closed_at = now,
                opening_cash_minor = openShift.OpeningCashMinor,
                expected_cash_minor = openShift.ExpectedCashMinor,
                actual_cash_minor = openShift.ActualCashMinor,
                difference_minor = command.ActualCashMinor - preview.ExpectedCashMinor,
                pending_hold_count = preview.PendingHoldCount,
                pending_return_count = preview.PendingReturnCount,
                count_id = count.Id,
                items = count.Lines.Select(value => new
                {
                    count_line_id = value.Id,
                    item_id = value.ItemId,
                    expected_scaled = value.ExpectedScaled,
                    actual_scaled = value.ActualScaled,
                    difference_scaled = value.DifferenceScaled,
                    quantity_scale = value.QuantityScale,
                    name_snapshot = value.NameSnapshot,
                    unit_snapshot = value.UnitSnapshot
                }).ToArray()
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new CloseShiftResult(openShift.Id, exportJob.Id, printJob.Id, await BuildReportAsync(db, openShift.Id, cancellationToken), false);
        }
        finally
        {
            database.WriteLock.Release();
        }
    }

    public async Task<IReadOnlyList<ClosedShiftSnapshot>> GetClosedShiftsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = database.CreateContext();
        var closedShifts = await db.Shifts.AsNoTracking()
            .Where(value => value.Status == ShiftStatus.Closed)
            .ToListAsync(cancellationToken);
        var shifts = closedShifts
            .OrderByDescending(value => value.BusinessDate)
            .ThenByDescending(value => value.OpenedAtUtc)
            .Take(250)
            .ToList();
        if (shifts.Count == 0) return [];
        var ids = shifts.Select(value => value.Id).ToArray();
        var sales = await db.Sales.AsNoTracking().Where(value => ids.Contains(value.ShiftId)).ToListAsync(cancellationToken);
        var corrections = await db.SaleCorrections.AsNoTracking().Where(value => ids.Contains(value.PostingShiftId)).ToListAsync(cancellationToken);
        var artifacts = await db.ReportArtifacts.AsNoTracking().Where(value => ids.Contains(value.ShiftId)).ToListAsync(cancellationToken);
        var jobs = await db.SideEffectJobs.AsNoTracking()
            .Where(value => ids.Contains(value.SourceId) && value.Kind == SideEffectKind.ExportShiftReport)
            .ToListAsync(cancellationToken);
        return shifts.Select(shift =>
        {
            var shiftSales = sales.Where(value => value.ShiftId == shift.Id).ToArray();
            var refund = corrections.Where(value => value.PostingShiftId == shift.Id).Sum(value => value.RefundTotalMinor);
            var artifact = artifacts.Where(value => value.ShiftId == shift.Id).OrderByDescending(value => value.ReportVersion).FirstOrDefault();
            var job = jobs.SingleOrDefault(value => value.SourceId == shift.Id);
            var reportStatus = artifact is not null ? "جاهز" : job?.State switch
            {
                SideEffectState.Failed => "فشل — يمكن إعادة المحاولة",
                SideEffectState.Running => "جارٍ الإنشاء",
                _ => "بانتظار الإنشاء"
            };
            return new ClosedShiftSnapshot(
                shift.Id,
                shift.Kind,
                shift.BusinessDate,
                shift.OpenedAtUtc,
                shift.ClosedAtUtc ?? shift.OpenedAtUtc,
                shiftSales.Sum(value => value.TotalMinor),
                refund,
                shiftSales.Length,
                shift.ExpectedCashMinor ?? 0,
                shift.ActualCashMinor ?? 0,
                reportStatus,
                artifact?.LocalPath,
                artifact?.ContentHash);
        }).ToArray();
    }

    public async Task<ShiftReportData> GetShiftReportDataAsync(Guid shiftId, CancellationToken cancellationToken = default)
    {
        await using var db = database.CreateContext();
        return await BuildReportAsync(db, shiftId, cancellationToken);
    }

    public async Task<Guid> GetOrResetExportJobAsync(Guid shiftId, CancellationToken cancellationToken = default)
    {
        if (shiftId == Guid.Empty) throw Rule("SHIFT_NOT_FOUND", "اختر وردية مغلقة أولاً.");
        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            var isClosed = await db.Shifts.AnyAsync(value => value.Id == shiftId && value.Status == ShiftStatus.Closed, cancellationToken);
            if (!isClosed) throw Rule("SHIFT_NOT_CLOSED", "لا يمكن إنشاء التقرير قبل إغلاق الوردية.");
            var job = await db.SideEffectJobs.SingleOrDefaultAsync(
                value => value.SourceId == shiftId && value.Kind == SideEffectKind.ExportShiftReport && value.DocumentVersion == 1,
                cancellationToken);
            if (job is null)
            {
                job = NewJob(shiftId, SideEffectKind.ExportShiftReport, DateTimeOffset.UtcNow);
                db.SideEffectJobs.Add(job);
            }
            else if (job.State != SideEffectState.Completed)
            {
                job.State = SideEffectState.Pending;
                job.NextAttemptAtUtc = DateTimeOffset.UtcNow;
                job.LastError = null;
            }
            await db.SaveChangesAsync(cancellationToken);
            return job.Id;
        }
        finally
        {
            database.WriteLock.Release();
        }
    }

    public async Task<Guid> GetOrResetPrintJobAsync(
        Guid sourceId,
        SideEffectKind kind,
        CancellationToken cancellationToken = default)
    {
        if (sourceId == Guid.Empty) throw Rule("PRINT_SOURCE_REQUIRED", "اختر المستند المطلوب طباعته أولاً.");
        if (kind is not (SideEffectKind.PrintReceipt or SideEffectKind.PrintCustomOrder or SideEffectKind.PrintShiftReport))
            throw Rule("INVALID_PRINT_JOB", "نوع مهمة الطباعة غير صالح.");

        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            var sourceExists = kind switch
            {
                SideEffectKind.PrintShiftReport => await db.Shifts.AnyAsync(value => value.Id == sourceId && value.Status == ShiftStatus.Closed, cancellationToken),
                SideEffectKind.PrintCustomOrder => await db.CustomOrders.AnyAsync(value => value.Id == sourceId, cancellationToken),
                _ => await db.Sales.AnyAsync(value => value.Id == sourceId, cancellationToken)
                    || await db.SaleCorrections.AnyAsync(value => value.Id == sourceId, cancellationToken)
            };
            if (!sourceExists)
                throw Rule("PRINT_SOURCE_NOT_FOUND", "لم يتم العثور على المستند المطلوب طباعته.");

            var job = await db.SideEffectJobs
                .Where(value => value.SourceId == sourceId && value.Kind == kind)
                .OrderByDescending(value => value.DocumentVersion)
                .FirstOrDefaultAsync(cancellationToken);
            if (job is null)
            {
                job = NewJob(sourceId, kind, DateTimeOffset.UtcNow);
                db.SideEffectJobs.Add(job);
            }
            job.State = SideEffectState.Pending;
            job.NextAttemptAtUtc = DateTimeOffset.UtcNow;
            job.LastError = null;
            job.CompletedAtUtc = null;
            await db.SaveChangesAsync(cancellationToken);
            return job.Id;
        }
        finally
        {
            database.WriteLock.Release();
        }
    }

    public async Task MarkReportSucceededAsync(
        Guid exportJobId,
        ShiftReportWriteResult result,
        CancellationToken cancellationToken = default)
    {
        if (exportJobId == Guid.Empty || string.IsNullOrWhiteSpace(result.Path)
            || result.Sha256.Length != 64 || result.ByteLength <= 0)
            throw Rule("INVALID_REPORT_RESULT", "بيانات ملف تقرير الوردية غير مكتملة.");
        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var job = await db.SideEffectJobs.SingleOrDefaultAsync(value => value.Id == exportJobId, cancellationToken)
                ?? throw Rule("REPORT_JOB_NOT_FOUND", "لم يتم العثور على مهمة تقرير الوردية.");
            if (job.Kind != SideEffectKind.ExportShiftReport)
                throw Rule("INVALID_REPORT_JOB", "المهمة المحددة ليست مهمة تصدير تقرير.");
            var existing = await db.ReportArtifacts.SingleOrDefaultAsync(
                value => value.ShiftId == job.SourceId && value.ReportVersion == job.DocumentVersion,
                cancellationToken);
            if (existing is not null)
            {
                if (!string.Equals(existing.ContentHash, result.Sha256, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(Path.GetFullPath(existing.LocalPath), Path.GetFullPath(result.Path), StringComparison.OrdinalIgnoreCase))
                    throw Rule("IMMUTABLE_REPORT_CONFLICT", "يوجد تقرير مختلف محفوظ لهذه الوردية. لم يتم استبدال الملف الأصلي.");
            }
            else
            {
                db.ReportArtifacts.Add(new ReportArtifact
                {
                    Id = Guid.NewGuid(),
                    ShiftId = job.SourceId,
                    ReportVersion = job.DocumentVersion,
                    ContentHash = result.Sha256.ToLowerInvariant(),
                    Format = "xlsx",
                    LocalPath = Path.GetFullPath(result.Path),
                    ByteLength = result.ByteLength,
                    GeneratedAtUtc = DateTimeOffset.UtcNow
                });
            }
            job.State = SideEffectState.Completed;
            job.CompletedAtUtc = DateTimeOffset.UtcNow;
            job.LastError = null;
            var uploadJob = await db.SideEffectJobs.SingleOrDefaultAsync(
                value => value.SourceId == job.SourceId
                    && value.Kind == SideEffectKind.UploadShiftReport
                    && value.DocumentVersion == job.DocumentVersion,
                cancellationToken);
            if (uploadJob is not null && uploadJob.State != SideEffectState.Completed)
            {
                uploadJob.State = SideEffectState.Pending;
                uploadJob.NextAttemptAtUtc = DateTimeOffset.UtcNow;
                uploadJob.LastError = null;
                uploadJob.CompletedAtUtc = null;
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            database.WriteLock.Release();
        }
    }

    public async Task MarkReportFailedAsync(Guid exportJobId, string error, CancellationToken cancellationToken = default)
    {
        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            var job = await db.SideEffectJobs.SingleOrDefaultAsync(value => value.Id == exportJobId, cancellationToken)
                ?? throw Rule("REPORT_JOB_NOT_FOUND", "لم يتم العثور على مهمة تقرير الوردية.");
            if (job.Kind != SideEffectKind.ExportShiftReport) throw Rule("INVALID_REPORT_JOB", "المهمة المحددة ليست مهمة تصدير تقرير.");
            job.State = SideEffectState.Failed;
            job.Attempts += 1;
            job.LastError = SafeError(error);
            job.NextAttemptAtUtc = DateTimeOffset.UtcNow.AddMinutes(Math.Min(30, Math.Pow(2, Math.Min(job.Attempts, 5))));
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            database.WriteLock.Release();
        }
    }

    public async Task MarkPrintSucceededAsync(Guid printJobId, CancellationToken cancellationToken = default)
    {
        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            var job = await db.SideEffectJobs.SingleOrDefaultAsync(value => value.Id == printJobId, cancellationToken)
                ?? throw Rule("PRINT_JOB_NOT_FOUND", "لم يتم العثور على مهمة الطباعة.");
            if (job.Kind is not (SideEffectKind.PrintReceipt or SideEffectKind.PrintCustomOrder or SideEffectKind.PrintShiftReport))
                throw Rule("INVALID_PRINT_JOB", "المهمة المحددة ليست مهمة طباعة.");
            job.State = SideEffectState.Completed;
            job.CompletedAtUtc = DateTimeOffset.UtcNow;
            job.LastError = null;
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            database.WriteLock.Release();
        }
    }

    public async Task MarkPrintFailedAsync(Guid printJobId, string error, CancellationToken cancellationToken = default)
    {
        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            var job = await db.SideEffectJobs.SingleOrDefaultAsync(value => value.Id == printJobId, cancellationToken)
                ?? throw Rule("PRINT_JOB_NOT_FOUND", "لم يتم العثور على مهمة الطباعة.");
            if (job.Kind is not (SideEffectKind.PrintReceipt or SideEffectKind.PrintCustomOrder or SideEffectKind.PrintShiftReport))
                throw Rule("INVALID_PRINT_JOB", "المهمة المحددة ليست مهمة طباعة.");
            job.State = SideEffectState.Failed;
            job.Attempts += 1;
            job.LastError = SafeError(error);
            job.NextAttemptAtUtc = DateTimeOffset.UtcNow.AddMinutes(Math.Min(30, Math.Pow(2, Math.Min(job.Attempts, 5))));
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            database.WriteLock.Release();
        }
    }

    public async Task<BranchSettingsSnapshot> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = database.CreateContext();
        var settings = await db.BranchLocalSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var configuration = await db.DeviceConfigurations.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return new BranchSettingsSnapshot(
            configuration?.ApiBaseUrl ?? string.Empty,
            settings?.ExportDirectory ?? new ReportDirectorySettings(configuration?.Profile == DeviceProfile.BranchType2 ? "Branch2" : "Branch1").GetDirectory(),
            settings?.PrinterName ?? string.Empty,
            configuration?.TouchMode ?? true);
    }

    public async Task SaveSettingsAsync(SaveBranchSettingsCommand command, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(command.ConnectionAddress.Trim().TrimEnd('/'), UriKind.Absolute, out var connectionAddress)
            || (connectionAddress.Scheme != Uri.UriSchemeHttps
                && !(connectionAddress.Scheme == Uri.UriSchemeHttp && connectionAddress.IsLoopback)))
            throw Rule("INVALID_CONNECTION_ADDRESS", "عنوان الاتصال غير صالح.");
        if (string.IsNullOrWhiteSpace(command.ExportDirectory))
            throw Rule("EXPORT_DIRECTORY_REQUIRED", "اختر مجلد حفظ تقارير Excel.");
        string exportDirectory;
        try { exportDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(command.ExportDirectory.Trim())); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw Rule("INVALID_EXPORT_DIRECTORY", "مسار مجلد التقارير غير صالح.");
        }
        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            var profile = await db.DeviceConfigurations.Select(x => x.Profile).SingleOrDefaultAsync(cancellationToken);
            new ReportDirectorySettings(profile == DeviceProfile.BranchType2 ? "Branch2" : "Branch1").SaveDirectory(exportDirectory);
            var settings = await db.BranchLocalSettings.SingleOrDefaultAsync(cancellationToken);
            if (settings is null)
            {
                settings = new BranchLocalSettings { Id = 1 };
                db.BranchLocalSettings.Add(settings);
            }
            settings.ExportDirectory = exportDirectory;
            settings.PrinterName = command.PrinterName.Trim();
            settings.UpdatedAtUtc = DateTimeOffset.UtcNow;
            var configuration = await db.DeviceConfigurations.SingleOrDefaultAsync(cancellationToken);
            if (configuration is not null)
            {
                configuration.ApiBaseUrl = connectionAddress.ToString().TrimEnd('/');
                configuration.TouchMode = command.TouchMode;
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            database.WriteLock.Release();
        }
    }

    private static async Task<ClosingPreviewSnapshot> BuildClosingPreviewAsync(
        BranchDbContext db,
        Shift shift,
        CancellationToken cancellationToken)
    {
        foreach (var item in shift.OpeningItems)
        {
            var calculated = checked(item.OpeningQuantityScaled + item.IncomingScaled
                + item.CustomerRestockScaled - item.SoldScaled - item.CafeIssuedScaled
                - item.KitchenReturnScaled - item.WasteScaled + item.AdjustmentScaled);
            if (calculated < 0 || calculated != item.ExpectedCloseScaled)
                throw Rule("STOCK_RECONCILIATION_FAILED", $"رصيد {item.NameSnapshot} غير متسق. لا تغلق الوردية قبل مراجعة الحركات.");
        }
        var sales = await db.Sales.AsNoTracking().Where(value => value.ShiftId == shift.Id).ToListAsync(cancellationToken);
        var corrections = await db.SaleCorrections.AsNoTracking().Where(value => value.PostingShiftId == shift.Id).ToListAsync(cancellationToken);
        var cashMovements = await db.CashMovements.AsNoTracking().Where(value => value.ShiftId == shift.Id).ToListAsync(cancellationToken);
        var pendingHolds = await db.StockHolds.AsNoTracking().CountAsync(value => value.Status != HoldStatus.Released, cancellationToken);
        var pendingReturns = await db.KitchenReturns.AsNoTracking().CountAsync(value => value.Status == KitchenReturnStatus.Dispatched, cancellationToken);
        var cashSales = cashMovements.Where(value => value.Kind == "SALE_CASH").Sum(value => value.AmountMinor);
        var visaSales = cashMovements.Where(value => value.Kind == "SALE_VISA").Sum(value => value.AmountMinor);
        var cashRefunds = -cashMovements.Where(value => value.Kind == "REFUND_CASH").Sum(value => value.AmountMinor);
        var visaRefunds = -cashMovements.Where(value => value.Kind == "REFUND_VISA").Sum(value => value.AmountMinor);
        if (shift.OpeningCashMinor + cashSales - cashRefunds < 0)
            throw Rule("NEGATIVE_EXPECTED_CASH", "النقدية المتوقعة سالبة؛ راجع المدفوعات والاستردادات قبل الإغلاق.");
        return new ClosingPreviewSnapshot(
            shift.Id,
            shift.Kind,
            shift.BusinessDate,
            shift.OpenedAtUtc,
            shift.OpeningCashMinor,
            cashSales,
            visaSales,
            cashRefunds,
            visaRefunds,
            shift.OpeningCashMinor + cashSales - cashRefunds,
            sales.Count,
            pendingHolds,
            pendingReturns,
            shift.OpeningItems.OrderBy(value => value.NameSnapshot).Select(value => new ClosingItemSnapshot(
                value.ItemId,
                value.NameSnapshot,
                value.UnitSnapshot,
                value.QuantityScale,
                value.OpeningQuantityScaled,
                value.IncomingScaled,
                value.SoldScaled,
                value.CafeIssuedScaled,
                value.CustomerRestockScaled,
                value.KitchenReturnScaled,
                value.WasteScaled,
                value.AdjustmentScaled,
                value.ExpectedCloseScaled)).ToArray());
    }

    private static async Task<ShiftReportData> BuildReportAsync(BranchDbContext db, Guid shiftId, CancellationToken cancellationToken)
    {
        var shift = await db.Shifts.AsNoTracking().Include(value => value.OpeningItems)
            .SingleOrDefaultAsync(value => value.Id == shiftId, cancellationToken)
            ?? throw Rule("SHIFT_NOT_FOUND", "لم يتم العثور على الوردية.");
        if (shift.Status != ShiftStatus.Closed || shift.ClosedAtUtc is null || shift.ExpectedCashMinor is null || shift.ActualCashMinor is null)
            throw Rule("SHIFT_NOT_CLOSED", "لا يمكن إنشاء التقرير قبل إغلاق الوردية.");
        var configuration = await db.DeviceConfigurations.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var cashMovements = await db.CashMovements.AsNoTracking().Where(value => value.ShiftId == shift.Id).ToListAsync(cancellationToken);
        var receiptCount = await db.Sales.AsNoTracking().CountAsync(value => value.ShiftId == shift.Id, cancellationToken);
        var pendingHolds = await db.StockHolds.AsNoTracking().CountAsync(value => value.Status != HoldStatus.Released, cancellationToken);
        var pendingReturns = await db.KitchenReturns.AsNoTracking().CountAsync(value => value.Status == KitchenReturnStatus.Dispatched, cancellationToken);
        var cashSales = cashMovements.Where(value => value.Kind == "SALE_CASH").Sum(value => value.AmountMinor);
        var visaSales = cashMovements.Where(value => value.Kind == "SALE_VISA").Sum(value => value.AmountMinor);
        var cashRefunds = -cashMovements.Where(value => value.Kind == "REFUND_CASH").Sum(value => value.AmountMinor);
        var visaRefunds = -cashMovements.Where(value => value.Kind == "REFUND_VISA").Sum(value => value.AmountMinor);
        return new ShiftReportData(
            shift.Id,
            1,
            configuration?.SiteName ?? "فرع نوع ١",
            shift.Kind,
            shift.BusinessDate,
            shift.OpenedAtUtc,
            shift.ClosedAtUtc.Value,
            shift.OpeningCashMinor,
            cashSales,
            visaSales,
            cashRefunds,
            visaRefunds,
            shift.ExpectedCashMinor.Value,
            shift.ActualCashMinor.Value,
            shift.ActualCashMinor.Value - shift.ExpectedCashMinor.Value,
            receiptCount,
            pendingHolds,
            pendingReturns,
            shift.OpeningItems.OrderBy(value => value.NameSnapshot).Select(value => new ShiftReportItemData(
                value.NameSnapshot,
                value.UnitSnapshot,
                value.QuantityScale,
                value.OpeningQuantityScaled,
                value.IncomingScaled,
                value.SoldScaled,
                value.CafeIssuedScaled,
                value.CustomerRestockScaled,
                value.KitchenReturnScaled,
                value.WasteScaled,
                value.AdjustmentScaled,
                value.ExpectedCloseScaled,
                value.ActualCloseScaled ?? value.ExpectedCloseScaled,
                value.DiscrepancyScaled ?? 0)).ToArray());
    }

    private static KitchenRequestSnapshot ToRequestSnapshot(
        KitchenRequest request,
        bool existing = false,
        RequestDeliveryState? deliveryState = null) => new(
        request.Id,
        request.Status,
        request.RequestedAtUtc,
        request.SubmittedAtUtc,
        request.Version,
        request.Lines.OrderBy(value => value.NameSnapshot).Select(value => new KitchenRequestLineSnapshot(
            value.Id,
            value.ItemId,
            value.NameSnapshot,
            value.UnitSnapshot,
            value.QuantityScale,
            value.RequestedScaled,
            value.ApprovedScaled,
            value.SentScaled)).ToArray(),
        deliveryState ?? (request.Status == KitchenRequestStatus.Draft ? RequestDeliveryState.Draft : RequestDeliveryState.Waiting),
        existing);

    private static RequestDeliveryState ResolveDeliveryState(KitchenRequest request, OutboxState? outboxState)
    {
        if (request.Status == KitchenRequestStatus.Draft) return RequestDeliveryState.Draft;
        if (request.Status is KitchenRequestStatus.Received or KitchenRequestStatus.Approved or KitchenRequestStatus.Rejected
            || request.Lines.Any(value => value.SentScaled > 0)) return RequestDeliveryState.Received;
        return outboxState switch
        {
            OutboxState.Acknowledged => RequestDeliveryState.Sent,
            OutboxState.Failed => RequestDeliveryState.Failed,
            _ => RequestDeliveryState.Waiting
        };
    }

    private static CatalogItemSnapshot ToCatalogSnapshot(CatalogItem item) => new(
        item.Id,
        item.Sku,
        item.NameAr,
        item.Unit,
        item.QuantityScale,
        item.RetailPriceMinor,
        item.Active,
        item.StockBalance?.QuantityScaled ?? 0,
        item.StockBalance?.Revision ?? 0,
        item.Version);

    private static IncomingShipmentSnapshot ToShipmentSnapshot(Shipment shipment)
    {
        var counted = shipment.Receipt?.Lines.ToDictionary(value => value.ShipmentLineId, value => (long?)value.CountedScaled)
            ?? new Dictionary<Guid, long?>();
        return new IncomingShipmentSnapshot(
            shipment.Id,
            shipment.Reference,
            shipment.Status,
            shipment.Version,
            shipment.DispatchedAtUtc,
            shipment.Lines.OrderBy(value => value.NameSnapshot).Select(value => new ShipmentLineSnapshot(
                value.Id,
                value.ItemId,
                value.NameSnapshot,
                value.UnitSnapshot,
                value.QuantityScale,
                value.SentScaled,
                counted.GetValueOrDefault(value.Id))).ToArray(),
            shipment.Receipt?.Id,
            shipment.Status is ShipmentStatus.Dispatched or ShipmentStatus.AwaitingReceipt && shipment.Receipt is null);
    }

    private static async Task<DeviceConfiguration> RequireBranchConfigurationAsync(BranchDbContext db, CancellationToken cancellationToken)
    {
        var configuration = await db.DeviceConfigurations.SingleOrDefaultAsync(cancellationToken)
            ?? throw Rule("DEVICE_NOT_ENROLLED", "سجّل جهاز الفرع أولاً.");
        if (configuration.Profile is not (DeviceProfile.BranchType1 or DeviceProfile.BranchType2))
            throw Rule("WRONG_PROFILE", "هذه العملية متاحة لأجهزة الفروع فقط.");
        return configuration;
    }

    private static async Task<Shift> RequireOpenShiftAsync(BranchDbContext db, CancellationToken cancellationToken) =>
        await db.Shifts.SingleOrDefaultAsync(value => value.Status == ShiftStatus.Open, cancellationToken)
        ?? throw Rule("SHIFT_REQUIRED", "افتح وردية قبل تنفيذ هذه العملية.");

    private static async Task<ShiftItemSnapshot> GetOrCreateShiftSnapshotAsync(
        BranchDbContext db,
        Shift shift,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var snapshot = await db.ShiftItemSnapshots.SingleOrDefaultAsync(
            value => value.ShiftId == shift.Id && value.ItemId == itemId,
            cancellationToken);
        if (snapshot is not null) return snapshot;
        var item = await db.CatalogItems.Include(value => value.StockBalance).SingleAsync(value => value.Id == itemId, cancellationToken);
        var profile = (await db.DeviceConfigurations.SingleAsync(cancellationToken)).Profile;
        var locations = profile == DeviceProfile.BranchType2 ? await db.LocationBalances.Where(x => x.ItemId == itemId).ToListAsync(cancellationToken) : [];
        var opening = profile == DeviceProfile.BranchType2 ? locations.Sum(x => x.QuantityScaled) : item.StockBalance?.QuantityScaled ?? 0;
        snapshot = new ShiftItemSnapshot
        {
            ShiftId = shift.Id,
            ItemId = item.Id,
            NameSnapshot = item.NameAr,
            UnitSnapshot = item.Unit,
            QuantityScale = item.QuantityScale,
            OpeningQuantityScaled = opening,
            ExpectedCloseScaled = opening
        };
        db.ShiftItemSnapshots.Add(snapshot);
        return snapshot;
    }

    private static SideEffectJob NewJob(Guid sourceId, SideEffectKind kind, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        SourceId = sourceId,
        Kind = kind,
        DocumentVersion = 1,
        State = SideEffectState.Pending,
        Attempts = 0,
        NextAttemptAtUtc = now,
        CreatedAtUtc = now
    };

    private static CustomOrderSnapshot ToCustomOrderSnapshot(
        CustomOrder order,
        IReadOnlyCollection<CustomOrder> allOrders,
        bool existing)
    {
        var customerOrders = allOrders.Where(value => order.CafeCustomerId is Guid customerId
                ? value.CafeCustomerId == customerId
                : NormalizeCustomerPhone(value.CustomerPhone) == NormalizeCustomerPhone(order.CustomerPhone))
            .ToArray();
        var totalPayments = customerOrders.SelectMany(value => value.Payments).Sum(value => value.AmountMinor);
        var charged = customerOrders.Where(value => value.Status != CustomOrderStatus.Cancelled).Sum(value => value.TotalMinor);
        var unallocatedPayment = totalPayments;
        var orderPaid = 0L;
        foreach (var invoice in customerOrders.Where(value => value.Status != CustomOrderStatus.Cancelled)
                     .OrderBy(value => value.CreatedAtUtc).ThenBy(value => value.Id))
        {
            var allocated = Math.Min(invoice.TotalMinor, Math.Max(0, unallocatedPayment));
            if (invoice.Id == order.Id) orderPaid = allocated;
            unallocatedPayment -= allocated;
        }
        var orderRemaining = order.Status == CustomOrderStatus.Cancelled ? 0 : order.TotalMinor - orderPaid;
        return new CustomOrderSnapshot(
            order.Id,
            order.CafeCustomerId,
            order.OrderNumber,
            order.CustomerName,
            order.CustomerPhone,
            order.Description,
            order.DueAtUtc,
            order.TotalMinor,
            orderPaid,
            orderRemaining,
            charged - totalPayments,
            order.Status,
            order.Version,
            order.CreatedAtUtc,
            order.UpdatedAtUtc,
            existing,
            order.Lines.OrderBy(value => value.ItemNameSnapshot).Select(value => new CustomOrderLineSnapshot(
                value.ItemId, value.ItemNameSnapshot, value.UnitSnapshot, value.QuantityScale,
                value.QuantityScaled, value.UnitPriceMinor, value.LineTotalMinor)).ToArray());
    }

    private static CafeProfileSnapshot ToCafeProfileSnapshot(CafeCustomer customer, IReadOnlyCollection<CustomOrder> allOrders)
    {
        var orders = allOrders.Where(value => value.CafeCustomerId == customer.Id).ToArray();
        var charged = orders.Where(value => value.Status != CustomOrderStatus.Cancelled).Sum(value => value.TotalMinor);
        var paid = orders.SelectMany(value => value.Payments).Sum(value => value.AmountMinor);
        return new CafeProfileSnapshot(
            customer.Id, customer.Name, customer.Kind, customer.Phone, customer.Address,
            charged - paid,
            orders.Count(value => value.Status is not (CustomOrderStatus.Delivered or CustomOrderStatus.Cancelled)),
            customer.Version);
    }

    private static async Task<CustomOrderSnapshot> LoadCustomOrderSnapshotAsync(
        BranchDbContext db,
        Guid orderId,
        bool existing,
        CancellationToken cancellationToken)
    {
        var orders = await db.CustomOrders.AsNoTracking().Include(value => value.Payments).Include(value => value.Lines).ToListAsync(cancellationToken);
        var order = orders.Single(value => value.Id == orderId);
        return ToCustomOrderSnapshot(order, orders, existing);
    }

    private static string NormalizeCustomerPhone(string value)
    {
        var normalized = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            if (character == '+' && normalized.Length == 0)
            {
                normalized.Append(character);
                continue;
            }
            if (!char.IsDigit(character)) continue;
            var digit = (int)char.GetNumericValue(character);
            if (digit is >= 0 and <= 9) normalized.Append((char)('0' + digit));
        }
        return normalized.ToString();
    }

    private static string CustomOrderStatusText(CustomOrderStatus status) => status switch
    {
        CustomOrderStatus.Confirmed => "تم تأكيد الطلب",
        CustomOrderStatus.Ready => "الطلب جاهز للاستلام",
        CustomOrderStatus.Delivered => "تم تسليم الطلب",
        CustomOrderStatus.Cancelled => "تم إلغاء الطلب",
        _ => "طلب جديد"
    };

    private static async Task<SequenceState> GetSequenceAsync(BranchDbContext db, CancellationToken cancellationToken)
    {
        var sequence = await db.SequenceStates.SingleOrDefaultAsync(cancellationToken);
        if (sequence is not null) return sequence;
        sequence = new SequenceState();
        db.SequenceStates.Add(sequence);
        return sequence;
    }

    private static void RequireLocationActor(Guid? userId, string? authorization)
    {
        if (userId is null || userId == Guid.Empty || string.IsNullOrWhiteSpace(authorization))
            throw Rule("AUTHORIZATION_REQUIRED", "سجل دخول مستخدم مخول قبل ترحيل حركة المخزون.");
    }

    private static BranchInventoryTransaction CreateStockIssue(BranchDbContext db, DeviceConfiguration configuration, Guid referenceId, Guid userId,
        Branch2TransactionKind kind, string reason, IEnumerable<QuantityInput> quantities, Dictionary<Guid, BranchLocationBalance> balances, DateTimeOffset now)
    {
        var ledger = new BranchInventoryTransaction { Id = Guid.NewGuid(), ReferenceId = referenceId, SiteId = configuration.SiteId, UserId = userId,
            Kind = kind, Reason = reason, OccurredAtUtc = now };
        foreach (var input in quantities.OrderBy(x => x.ItemId))
        {
            var balance = balances[input.ItemId];
            if (balance.QuantityScaled < input.QuantityScaled) throw Rule("INSUFFICIENT_STOCK", "المخزون لا يكفي لهذه الحركة.");
            balance.QuantityScaled -= input.QuantityScaled;
            balance.Version = checked(balance.Version + 1);
            ledger.Lines.Add(new BranchInventoryTransactionLine { Id = Guid.NewGuid(), ItemId = input.ItemId, Location = BranchInventoryLocation.Stock, DeltaScaled = -input.QuantityScaled });
        }
        db.InventoryTransactions.Add(ledger);
        return ledger;
    }

    private static void QueueLocationTransaction(BranchDbContext db, SequenceState sequence, BranchInventoryTransaction ledger, string authorization) =>
        QueueEvent(db, sequence, ledger.Id, "branch2.inventory.posted", ledger.OccurredAtUtc, new {
            transaction_id = ledger.Id, reference_id = ledger.ReferenceId, user_id = ledger.UserId, authorization,
            kind = ledger.Kind.ToString(), reason = ledger.Reason,
            lines = ledger.Lines.Select(x => new { item_id = x.ItemId, location = "FREEZER", delta_scaled = x.DeltaScaled.ToString(System.Globalization.CultureInfo.InvariantCulture) }).ToArray()
        }, ledger.Id);

    private static void QueueStockIssue(BranchDbContext db, SequenceState sequence, DeviceConfiguration configuration, Guid referenceId, Guid userId,
        string authorization, Branch2TransactionKind kind, string reason, IEnumerable<QuantityInput> quantities, Dictionary<Guid, BranchLocationBalance> balances, DateTimeOffset now) =>
        QueueLocationTransaction(db, sequence, CreateStockIssue(db, configuration, referenceId, userId, kind, reason, quantities, balances, now), authorization);

    private static void QueueEvent(
        BranchDbContext db,
        SequenceState sequence,
        Guid aggregateId,
        string eventType,
        DateTimeOffset occurredAtUtc,
        object payload,
        Guid? eventId = null)
    {
        var contractEvent = ContractEventFactory.Create(eventId ?? Guid.NewGuid(), sequence.NextDeviceSequence, eventType, occurredAtUtc, payload);
        sequence.NextDeviceSequence += 1;
        db.OutboxMessages.Add(new OutboxMessage
        {
            EventId = contractEvent.Id,
            AggregateId = aggregateId,
            DeviceSequence = contractEvent.DeviceSequence,
            EventType = contractEvent.EventType,
            SchemaVersion = contractEvent.SchemaVersion,
            OccurredAtUtc = contractEvent.OccurredAtUtc,
            PayloadJson = contractEvent.PayloadJson,
            DependenciesJson = contractEvent.DependenciesJson,
            ContentHash = contractEvent.ContentHash,
            State = OutboxState.Pending,
            NextAttemptAtUtc = occurredAtUtc
        });
    }

    private static void ValidateCommandId(Guid commandId)
    {
        if (commandId == Guid.Empty) throw Rule("INVALID_COMMAND_ID", "هوية العملية غير صالحة. أعد فتح الشاشة وحاول مرة أخرى.");
    }

    private static void ValidateQuantityInputs(IReadOnlyList<QuantityInput> inputs, string message)
    {
        if (inputs.Count == 0
            || inputs.Any(value => value.ItemId == Guid.Empty || value.QuantityScaled <= 0)
            || inputs.Select(value => value.ItemId).Distinct().Count() != inputs.Count)
            throw Rule("INVALID_QUANTITIES", message);
    }

    private static void EnsureSameQuantities(IEnumerable<QuantityInput> requested, IEnumerable<QuantityInput> committed)
    {
        var first = requested.OrderBy(value => value.ItemId).ToArray();
        var second = committed.OrderBy(value => value.ItemId).ToArray();
        if (first.Length != second.Length
            || first.Zip(second).Any(value => value.First.ItemId != value.Second.ItemId || value.First.QuantityScaled != value.Second.QuantityScaled))
            throw IdempotencyReuse();
    }

    private static BusinessRuleException IdempotencyReuse() =>
        Rule("IDEMPOTENCY_KEY_REUSE", "تم استخدام هوية العملية سابقاً ببيانات مختلفة. لم تتكرر أي حركة.");

    private static BusinessRuleException Rule(string code, string message) => new(code, message);

    private static long RoundMinor(decimal value) => checked((long)Math.Round(value, 0, MidpointRounding.AwayFromZero));

    private static string SafeError(string error)
    {
        var normalized = string.IsNullOrWhiteSpace(error) ? "فشل غير محدد" : error.Trim();
        return normalized.Length <= 1000 ? normalized : normalized[..1000];
    }

    private static string DefaultExportDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Sugar ERP",
        "تقارير الورديات");

    private static string CairoBusinessDate(DateTimeOffset utc)
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"); }
        catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"); }
        return TimeZoneInfo.ConvertTime(utc, zone).ToString("yyyy-MM-dd");
    }
}
