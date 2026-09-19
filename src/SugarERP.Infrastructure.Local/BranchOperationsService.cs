using Microsoft.EntityFrameworkCore;
using SugarERP.Application;
using SugarERP.Domain;

namespace SugarERP.Infrastructure.Local;

public sealed class BranchOperationsService(LocalDatabase database) : IBranchOperations
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) => database.InitializeAsync(cancellationToken);

    public async Task<BranchSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await using var db = database.CreateContext();
        var configuration = await db.DeviceConfigurations.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var shift = await db.Shifts.AsNoTracking().SingleOrDefaultAsync(value => value.Status == ShiftStatus.Open, cancellationToken);
        OpenShiftSnapshot? openShift = null;
        if (shift is not null)
        {
            var totals = await db.Sales.AsNoTracking()
                .Where(value => value.ShiftId == shift.Id && value.Status == SaleStatus.Posted)
                .GroupBy(_ => 1)
                .Select(group => new { Total = group.Sum(value => value.TotalMinor), Count = group.Count() })
                .SingleOrDefaultAsync(cancellationToken);
            openShift = new OpenShiftSnapshot(shift.Id, shift.Kind, shift.BusinessDate, shift.OpenedAtUtc, shift.OpeningCashMinor, totals?.Total ?? 0, totals?.Count ?? 0);
        }
        var items = await db.CatalogItems.AsNoTracking()
            .Include(value => value.StockBalance)
            .Where(value => value.Active || (value.StockBalance != null && value.StockBalance.QuantityScaled != 0))
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
        if (configuration?.Profile == DeviceProfile.BranchType2)
        {
            var display = await db.LocationBalances.AsNoTracking().Where(x => x.Location == BranchInventoryLocation.Display).ToDictionaryAsync(x => x.ItemId, cancellationToken);
            items = items.Select(x => x with { QuantityScaled = display.GetValueOrDefault(x.Id)?.QuantityScaled ?? 0, StockRevision = display.GetValueOrDefault(x.Id)?.Version ?? 1 }).ToList();
        }
        var pending = db.OutboxMessages.AsNoTracking().Where(value => value.State != OutboxState.Acknowledged);
        var pendingCount = await pending.CountAsync(cancellationToken);
        // SQLite cannot aggregate DateTimeOffset values. The outbox is small on a branch
        // workstation, so compute this display-only value after materialization.
        var pendingDates = await pending.Select(value => value.OccurredAtUtc).ToListAsync(cancellationToken);
        var oldest = pendingDates.Count == 0 ? (DateTimeOffset?)null : pendingDates.Min();
        return new BranchSnapshot(configuration, openShift, items, pendingCount, oldest);
    }

    public async Task SeedSyntheticDemoAsync(CancellationToken cancellationToken = default)
    {
        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            if (!await db.DeviceConfigurations.AnyAsync(cancellationToken))
            {
                db.DeviceConfigurations.Add(new DeviceConfiguration
                {
                    SiteId = Guid.Parse("20000000-0000-4000-8000-000000000001"),
                    DeviceId = Guid.Parse("30000000-0000-4000-8000-000000000001"),
                    Profile = DeviceProfile.BranchType1,
                    SiteName = "فرع الزمالك — عرض محلي",
                    ApiBaseUrl = "http://localhost:3001/api/v1",
                    DeviceCredential = DeviceCredentialProtector.DemoCredential,
                    TouchMode = true,
                    EnrolledAtUtc = DateTimeOffset.UtcNow
                });
            }
            if (!await db.SequenceStates.AnyAsync(cancellationToken)) db.SequenceStates.Add(new SequenceState());
            if (!await db.CatalogItems.AnyAsync(cancellationToken))
            {
                var now = DateTimeOffset.UtcNow;
                var demo = new[]
                {
                    (Guid.Parse("10000000-0000-4000-8000-000000000001"), "CAKE-CHOC", "تورتة شوكولاتة", 45000L, 18L),
                    (Guid.Parse("10000000-0000-4000-8000-000000000002"), "GATEAUX-CHOCO", "جاتوه شوكولاتة", 8500L, 46L),
                    (Guid.Parse("10000000-0000-4000-8000-000000000003"), "CUPCAKE-PINK", "كب كيك فراولة", 6500L, 32L),
                    (Guid.Parse("10000000-0000-4000-8000-000000000004"), "CHEESECAKE", "تشيز كيك", 12000L, 15L),
                    (Guid.Parse("10000000-0000-4000-8000-000000000005"), "COOKIES-BOX", "علبة كوكيز", 9500L, 24L),
                    (Guid.Parse("10000000-0000-4000-8000-000000000006"), "CROISSANT", "كرواسون", 5500L, 28L)
                };
                foreach (var (id, sku, name, price, quantity) in demo)
                {
                    db.CatalogItems.Add(new CatalogItem { Id = id, Sku = sku, NameAr = name, Unit = "قطعة", QuantityScale = 1, RetailPriceMinor = price, UpdatedAtUtc = now });
                    db.StockBalances.Add(new StockBalance { ItemId = id, QuantityScaled = quantity, Revision = 1, AsOfUtc = now });
                    db.StockMovements.Add(new StockMovement { Id = Guid.NewGuid(), DocumentId = Guid.Empty, SourceLineId = id, ItemId = id, Kind = StockMovementKind.InitialBalance, DeltaScaled = quantity, OccurredAtUtc = now });
                }
            }
            await db.SaveChangesAsync(cancellationToken);
            var demoItems = await db.CatalogItems.AsNoTracking().Where(value => value.Active).ToListAsync(cancellationToken);
            var demoCustomers = new[]
            {
                (Guid.Parse("41000000-0000-4000-8000-000000000001"), "كافيه النيل", "كافيه", "01090000001", "الزمالك", 75L),
                (Guid.Parse("41000000-0000-4000-8000-000000000002"), "سوبر ماركت روز", "سوبر ماركت", "01090000002", "الدقي", 70L)
            };
            foreach (var (id, name, kind, phone, address, percentage) in demoCustomers)
            {
                if (await db.CafeCustomers.AnyAsync(value => value.Id == id || value.Phone == phone, cancellationToken)) continue;
                var now = DateTimeOffset.UtcNow;
                var customer = new CafeCustomer
                {
                    Id = id, CommandId = Guid.NewGuid(), Name = name, Kind = kind, Phone = phone, Address = address,
                    Active = true, Version = 1, CreatedAtUtc = now, UpdatedAtUtc = now
                };
                foreach (var item in demoItems)
                    customer.Prices.Add(new CafePrice
                    {
                        Id = Guid.NewGuid(), CafeCustomerId = customer.Id, ItemId = item.Id,
                        UnitPriceMinor = item.RetailPriceMinor * percentage / 100, UpdatedAtUtc = now
                    });
                db.CafeCustomers.Add(customer);
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            database.WriteLock.Release();
        }
    }

    public async Task<OpenShiftSnapshot> OpenShiftAsync(ShiftKind kind, long openingCashMinor, CancellationToken cancellationToken = default)
    {
        if (openingCashMinor < 0) throw new BusinessRuleException("INVALID_OPENING_CASH", "قيمة العهدة الافتتاحية لا يمكن أن تكون سالبة.");
        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            if (await db.Shifts.AnyAsync(value => value.Status == ShiftStatus.Open, cancellationToken))
                throw new BusinessRuleException("SHIFT_ALREADY_OPEN", "هناك وردية مفتوحة بالفعل.");
            var now = DateTimeOffset.UtcNow;
            var shift = new Shift
            {
                Id = Guid.NewGuid(),
                Kind = kind,
                BusinessDate = CairoBusinessDate(now),
                OpenedAtUtc = now,
                OpeningCashMinor = openingCashMinor
            };
            var profile = (await db.DeviceConfigurations.SingleAsync(cancellationToken)).Profile;
            var locations = profile == DeviceProfile.BranchType2 ? await db.LocationBalances.AsNoTracking().ToListAsync(cancellationToken) : [];
            var heldItems = locations.Where(x => x.QuantityScaled != 0).Select(x => x.ItemId).Distinct().ToArray();
            var items = await db.CatalogItems.Include(value => value.StockBalance).Where(value => value.Active || value.StockBalance!.QuantityScaled != 0 || heldItems.Contains(value.Id)).ToListAsync(cancellationToken);
            foreach (var item in items)
            {
                var opening = profile == DeviceProfile.BranchType2 ? locations.Where(x => x.ItemId == item.Id).Sum(x => x.QuantityScaled) : item.StockBalance?.QuantityScaled ?? 0;
                shift.OpeningItems.Add(new ShiftItemSnapshot
                {
                    ShiftId = shift.Id,
                    ItemId = item.Id,
                    NameSnapshot = item.NameAr,
                    UnitSnapshot = item.Unit,
                    QuantityScale = item.QuantityScale,
                    OpeningQuantityScaled = opening,
                    ExpectedCloseScaled = opening
                });
            }
            db.Shifts.Add(shift);
            var sequence = await GetSequenceAsync(db, cancellationToken);
            QueueEvent(db, sequence, shift.Id, "shift.opened", now, new { shift_id = shift.Id, kind = kind == ShiftKind.Morning ? "MORNING" : "EVENING", business_date = shift.BusinessDate, opening_cash_minor = openingCashMinor });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new OpenShiftSnapshot(shift.Id, shift.Kind, shift.BusinessDate, shift.OpenedAtUtc, shift.OpeningCashMinor, 0, 0);
        }
        finally
        {
            database.WriteLock.Release();
        }
    }

    public async Task<SaleReceipt> CompleteSaleAsync(CompleteSaleCommand command, CancellationToken cancellationToken = default)
    {
        if (command.CommandId == Guid.Empty)
            throw new BusinessRuleException("INVALID_COMMAND_ID", "تعذر حفظ الطلب بهوية فارغة. أعد فتح شاشة البيع وحاول مرة أخرى.");
        if (command.Lines.Count == 0) throw new BusinessRuleException("EMPTY_CART", "أضف صنفاً واحداً على الأقل قبل الدفع.");
        if (command.Lines.Any(value => value.QuantityScaled <= 0) || command.Lines.Select(value => value.ItemId).Distinct().Count() != command.Lines.Count)
            throw new BusinessRuleException("INVALID_CART", "راجع كميات الأصناف في الطلب.");
        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            var existing = await db.Sales.AsNoTracking()
                .Include(value => value.Lines)
                .SingleOrDefaultAsync(value => value.CommandId == command.CommandId, cancellationToken);
            if (existing is not null)
            {
                var requestedLines = command.Lines.OrderBy(value => value.ItemId).ToArray();
                var committedLines = existing.Lines.OrderBy(value => value.ItemId).ToArray();
                var sameCommand = existing.PaymentMethod == command.PaymentMethod
                    && existing.Fulfillment == command.Fulfillment
                    && existing.DiscountMinor == command.DiscountMinor
                    && existing.TipMinor == command.TipMinor
                    && requestedLines.Length == committedLines.Length
                    && requestedLines.Zip(committedLines).All(pair =>
                        pair.First.ItemId == pair.Second.ItemId
                        && pair.First.QuantityScaled == pair.Second.QuantityScaled);
                if (!sameCommand)
                    throw new BusinessRuleException("IDEMPOTENCY_KEY_REUSE", "تم استخدام هوية هذا الطلب سابقاً ببيانات مختلفة. لم يتم تسجيل بيع جديد.");
                return ToReceipt(existing, true);
            }
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var shift = await db.Shifts.SingleOrDefaultAsync(value => value.Status == ShiftStatus.Open, cancellationToken)
                ?? throw new BusinessRuleException("SHIFT_REQUIRED", "ابدأ الوردية قبل تسجيل أي بيع.");
            var configuration = await db.DeviceConfigurations.SingleAsync(cancellationToken);
            var type2 = configuration.Profile == DeviceProfile.BranchType2;
            if (type2 && (command.UserId is null || command.UserId == Guid.Empty || string.IsNullOrWhiteSpace(command.Authorization)))
                throw new BusinessRuleException("OPERATOR_AUTHORIZATION_REQUIRED", "سجل دخول مستخدم لديه صلاحية البيع.");
            var itemIds = command.Lines.Select(value => value.ItemId).ToArray();
            var display = type2 ? await db.LocationBalances.Where(x => x.Location == BranchInventoryLocation.Display && itemIds.Contains(x.ItemId)).ToDictionaryAsync(x => x.ItemId, cancellationToken) : [];
            var items = await db.CatalogItems.Include(value => value.StockBalance).Where(value => itemIds.Contains(value.Id)).ToDictionaryAsync(value => value.Id, cancellationToken);
            if (items.Count != itemIds.Length || items.Values.Any(value => !value.Active))
                throw new BusinessRuleException("ITEM_UNAVAILABLE", "أحد الأصناف لم يعد متاحاً للبيع. حدّث الكتالوج.");
            var grossLines = command.Lines.Select(line =>
            {
                var item = items[line.ItemId];
                if (item.RetailPriceMinor <= 0)
                    throw new BusinessRuleException("ITEM_UNPRICED", $"لا يوجد سعر بيع للصنف {item.NameAr}. اطلب من الإدارة تحديد السعر أولاً.");
                if (type2 ? display.GetValueOrDefault(item.Id)?.QuantityScaled < line.QuantityScaled || !display.ContainsKey(item.Id) : item.StockBalance is null || item.StockBalance.QuantityScaled < line.QuantityScaled)
                    throw new BusinessRuleException("INSUFFICIENT_STOCK", $"الكمية المتاحة من {item.NameAr} غير كافية.");
                var gross = RoundMinor((decimal)item.RetailPriceMinor * line.QuantityScaled / item.QuantityScale);
                return (Cart: line, Item: item, Gross: gross);
            }).ToArray();
            var subtotal = grossLines.Sum(value => value.Gross);
            if (command.DiscountMinor < 0 || command.DiscountMinor > subtotal || command.TipMinor < 0)
                throw new BusinessRuleException("INVALID_TOTALS", "راجع الخصم والإكرامية قبل الدفع.");
            var now = DateTimeOffset.UtcNow;
            var sequence = await GetSequenceAsync(db, cancellationToken);
            var sale = new Sale
            {
                Id = Guid.NewGuid(),
                CommandId = command.CommandId,
                ShiftId = shift.Id,
                ReceiptNumber = $"{shift.BusinessDate.Replace("-", string.Empty, StringComparison.Ordinal)}-T1-{sequence.NextReceiptSequence:D4}",
                BusinessDate = shift.BusinessDate,
                PaymentMethod = command.PaymentMethod,
                Fulfillment = command.Fulfillment,
                SubtotalMinor = subtotal,
                DiscountMinor = command.DiscountMinor,
                TipMinor = command.TipMinor,
                TotalMinor = subtotal - command.DiscountMinor + command.TipMinor,
                OccurredAtUtc = now
            };
            sequence.NextReceiptSequence += 1;
            BranchInventoryTransaction? locationTransaction = type2 ? new BranchInventoryTransaction {
                Id = Guid.NewGuid(), ReferenceId = sale.Id, SiteId = configuration.SiteId, UserId = command.UserId!.Value,
                Kind = Branch2TransactionKind.RetailSale, Reason = "Retail sale from display", Fingerprint = command.CommandId.ToString(), OccurredAtUtc = now
            } : null;
            long allocated = 0;
            for (var index = 0; index < grossLines.Length; index += 1)
            {
                var row = grossLines[index];
                var lineId = Guid.NewGuid();
                var discount = index == grossLines.Length - 1 ? command.DiscountMinor - allocated : command.DiscountMinor * row.Gross / subtotal;
                allocated += discount;
                sale.Lines.Add(new SaleLine
                {
                    Id = lineId,
                    SaleId = sale.Id,
                    ItemId = row.Item.Id,
                    QuantityScaled = row.Cart.QuantityScaled,
                    QuantityScale = row.Item.QuantityScale,
                    UnitPriceMinor = row.Item.RetailPriceMinor,
                    AllocatedDiscountMinor = discount,
                    TotalMinor = row.Gross - discount,
                    NameSnapshot = row.Item.NameAr,
                    SkuSnapshot = row.Item.Sku,
                    UnitSnapshot = row.Item.Unit
                });
                if (locationTransaction is not null)
                {
                    var balance = display[row.Item.Id]; balance.QuantityScaled -= row.Cart.QuantityScaled; balance.Version++;
                    locationTransaction.Lines.Add(new BranchInventoryTransactionLine { Id = Guid.NewGuid(), ItemId = row.Item.Id, Location = BranchInventoryLocation.Display, DeltaScaled = -row.Cart.QuantityScaled });
                }
                else
                {
                    row.Item.StockBalance!.QuantityScaled -= row.Cart.QuantityScaled; row.Item.StockBalance.Revision += 1; row.Item.StockBalance.AsOfUtc = now;
                }
                var snapshot = await db.ShiftItemSnapshots.SingleAsync(value => value.ShiftId == shift.Id && value.ItemId == row.Item.Id, cancellationToken);
                snapshot.SoldScaled += row.Cart.QuantityScaled;
                snapshot.ExpectedCloseScaled -= row.Cart.QuantityScaled;
                db.StockMovements.Add(new StockMovement { Id = Guid.NewGuid(), DocumentId = sale.Id, SourceLineId = lineId, ItemId = row.Item.Id, ShiftId = shift.Id, Kind = StockMovementKind.RetailSale, DeltaScaled = -row.Cart.QuantityScaled, OccurredAtUtc = now });
            }
            sale.Payments.Add(new SalePayment { Id = Guid.NewGuid(), SaleId = sale.Id, Method = command.PaymentMethod, AmountMinor = sale.TotalMinor, PaidAtUtc = now });
            db.CashMovements.Add(new CashMovement { Id = Guid.NewGuid(), ShiftId = shift.Id, SourceId = sale.Id, Kind = command.PaymentMethod == PaymentMethod.Cash ? "SALE_CASH" : "SALE_VISA", AmountMinor = sale.TotalMinor, OccurredAtUtc = now });
            db.Sales.Add(sale);
            db.SideEffectJobs.Add(new SideEffectJob
            {
                Id = Guid.NewGuid(),
                SourceId = sale.Id,
                Kind = SideEffectKind.PrintReceipt,
                DocumentVersion = 1,
                State = SideEffectState.Pending,
                CreatedAtUtc = now,
                NextAttemptAtUtc = now
            });
            QueueEvent(db, sequence, sale.Id, "sale.completed", now, new
            {
                sale_id = sale.Id,
                shift_id = shift.Id,
                receipt_number = sale.ReceiptNumber,
                business_date = sale.BusinessDate,
                shift_kind = shift.Kind == ShiftKind.Morning ? "MORNING" : "EVENING",
                payment_method = command.PaymentMethod == PaymentMethod.Cash ? "CASH" : "VISA",
                fulfillment = command.Fulfillment == FulfillmentKind.Table ? "TABLE" : "TAKEAWAY",
                subtotal_minor = sale.SubtotalMinor,
                discount_minor = sale.DiscountMinor,
                tip_minor = sale.TipMinor,
                total_minor = sale.TotalMinor,
                lines = sale.Lines.Select(line => new { line_id = line.Id, item_id = line.ItemId, quantity_scaled = line.QuantityScaled, quantity_scale = line.QuantityScale, unit_price_minor = line.UnitPriceMinor, allocated_discount_minor = line.AllocatedDiscountMinor, total_minor = line.TotalMinor, name_snapshot = line.NameSnapshot, sku_snapshot = line.SkuSnapshot, unit_snapshot = line.UnitSnapshot }).ToArray()
            });
            if (locationTransaction is not null)
            {
                db.InventoryTransactions.Add(locationTransaction);
                QueueEvent(db, sequence, locationTransaction.Id, "branch2.inventory.posted", now, new {
                    transaction_id = locationTransaction.Id, reference_id = sale.Id, user_id = locationTransaction.UserId, authorization = command.Authorization,
                    kind = "RetailSale", reason = locationTransaction.Reason,
                    lines = locationTransaction.Lines.Select(x => new { item_id = x.ItemId, location = "DISPLAY", delta_scaled = x.DeltaScaled.ToString() }).ToArray()
                }, locationTransaction.Id);
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToReceipt(sale, false);
        }
        finally
        {
            database.WriteLock.Release();
        }
    }

    public async Task SaveEnrollmentAsync(EnrollmentCommand command, EnrollmentResult result, CancellationToken cancellationToken = default)
    {
        if (result.Profile != DeviceProfile.BranchType1) throw new BusinessRuleException("WRONG_PROFILE", "رمز التسجيل ليس مخصصاً لفرع نوع ١.");
        await database.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = database.CreateContext();
            if (await db.DeviceConfigurations.AnyAsync(cancellationToken)) throw new BusinessRuleException("ALREADY_ENROLLED", "هذا الجهاز مسجل بالفعل.");
            db.DeviceConfigurations.Add(new DeviceConfiguration { SiteId = result.SiteId, DeviceId = result.DeviceId, Profile = result.Profile, SiteName = "فرع نوع ١", ApiBaseUrl = command.ApiBaseUrl.ToString().TrimEnd('/'), DeviceCredential = DeviceCredentialProtector.Protect(result.Credential), StreamEpoch = result.StreamEpoch, TouchMode = command.TouchMode, EnrolledAtUtc = DateTimeOffset.UtcNow });
            db.SequenceStates.Add(new SequenceState());
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            database.WriteLock.Release();
        }
    }

    public async Task SetTouchModeAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        await using var db = database.CreateContext();
        var configuration = await db.DeviceConfigurations.SingleOrDefaultAsync(cancellationToken);
        if (configuration is null) return;
        configuration.TouchMode = enabled;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<SequenceState> GetSequenceAsync(BranchDbContext db, CancellationToken cancellationToken)
    {
        var sequence = await db.SequenceStates.SingleOrDefaultAsync(cancellationToken);
        if (sequence is not null) return sequence;
        sequence = new SequenceState();
        db.SequenceStates.Add(sequence);
        return sequence;
    }

    private static void QueueEvent(BranchDbContext db, SequenceState sequence, Guid aggregateId, string eventType, DateTimeOffset occurredAtUtc, object payload, Guid? eventId = null)
    {
        var contractEvent = ContractEventFactory.Create(eventId ?? Guid.NewGuid(), sequence.NextDeviceSequence, eventType, occurredAtUtc, payload);
        sequence.NextDeviceSequence += 1;
        db.OutboxMessages.Add(new OutboxMessage { EventId = contractEvent.Id, AggregateId = aggregateId, DeviceSequence = contractEvent.DeviceSequence, EventType = contractEvent.EventType, SchemaVersion = contractEvent.SchemaVersion, OccurredAtUtc = contractEvent.OccurredAtUtc, PayloadJson = contractEvent.PayloadJson, DependenciesJson = contractEvent.DependenciesJson, ContentHash = contractEvent.ContentHash, State = OutboxState.Pending, NextAttemptAtUtc = occurredAtUtc });
    }

    private static SaleReceipt ToReceipt(Sale sale, bool existing) => new(sale.Id, sale.ReceiptNumber, sale.SubtotalMinor, sale.DiscountMinor, sale.TipMinor, sale.TotalMinor, sale.PaymentMethod, sale.OccurredAtUtc, existing);

    private static long RoundMinor(decimal value) => checked((long)Math.Round(value, 0, MidpointRounding.AwayFromZero));

    private static string CairoBusinessDate(DateTimeOffset utc)
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"); }
        catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"); }
        return TimeZoneInfo.ConvertTime(utc, zone).ToString("yyyy-MM-dd");
    }
}
