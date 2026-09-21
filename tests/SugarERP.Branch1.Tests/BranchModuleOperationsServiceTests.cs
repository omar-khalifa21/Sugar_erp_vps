using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SugarERP.Application;
using SugarERP.Domain;
using SugarERP.Infrastructure.Local;
using Xunit;

namespace SugarERP.Branch1.Tests;

public sealed class BranchModuleOperationsServiceTests
{
    private static readonly Guid ChocolateCakeId = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid ChocolateGateauxId = Guid.Parse("10000000-0000-4000-8000-000000000002");

    [Fact]
    public async Task CatalogEditing_IsLocalFirstVersionedAndArchivesWithoutDeletingHistory()
    {
        await using var store = await ModuleTestStore.CreateAsync();
        var createCommandId = Guid.NewGuid();
        var created = await store.Modules.SaveCatalogItemAsync(new SaveCatalogItemCommand(
            createCommandId, null, null, "NEW-001", "صنف تجريبي", "قطعة", 1, 12_500));
        var replay = await store.Modules.SaveCatalogItemAsync(new SaveCatalogItemCommand(
            createCommandId, null, null, "NEW-001", "صنف تجريبي", "قطعة", 1, 12_500));

        Assert.Equal(created.Id, replay.Id);
        Assert.True(created.Active);
        Assert.Equal(0, created.QuantityScaled);

        var updated = await store.Modules.SaveCatalogItemAsync(new SaveCatalogItemCommand(
            Guid.NewGuid(), created.Id, created.Version, "NEW-001", "صنف تجريبي معدل", "قطعة", 1, 13_000));
        var archived = await store.Modules.ArchiveCatalogItemAsync(Guid.NewGuid(), updated.Id, updated.Version);

        Assert.False(archived.Active);
        Assert.Equal(updated.Version + 1, archived.Version);
        var catalog = await store.Modules.GetCatalogAsync();
        Assert.Contains(catalog.Items, value => value.Id == created.Id && !value.Active);

        await using var db = store.Database.CreateContext();
        Assert.Equal(1, await db.CatalogItems.CountAsync(value => value.Id == created.Id));
        Assert.Equal(3, await db.OutboxMessages.CountAsync(value => value.AggregateId == created.Id && value.EventType == "catalog.item.updated"));
        Assert.Empty(await db.StockMovements.Where(value => value.ItemId == created.Id).ToListAsync());
    }

    [Fact]
    public async Task DeleteCatalogItemAsync_DeletesOnlyNeverUsedZeroStockItem()
    {
        await using var store = await ModuleTestStore.CreateAsync();
        var created = await store.Modules.SaveCatalogItemAsync(new SaveCatalogItemCommand(
            Guid.NewGuid(), null, null, "DELETE-ME", "صنف للحذف", "قطعة", 1, 1_000));

        await store.Modules.DeleteCatalogItemAsync(Guid.NewGuid(), created.Id, created.Version);

        await using var db = store.Database.CreateContext();
        Assert.False(await db.CatalogItems.AnyAsync(value => value.Id == created.Id));
        Assert.False(await db.StockBalances.AnyAsync(value => value.ItemId == created.Id));
        Assert.Single(await db.OutboxMessages.Where(value => value.AggregateId == created.Id && value.EventType == "catalog.item.deleted").ToListAsync());

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            store.Modules.DeleteCatalogItemAsync(Guid.NewGuid(), ChocolateCakeId, expectedVersion: 1));
        Assert.Equal("ITEM_HAS_HISTORY", exception.Code);
    }

    [Fact]
    public async Task SubmittedWaredRequest_ShowsWaitingUntilOutboxIsAcknowledged()
    {
        await using var store = await ModuleTestStore.CreateAsync();
        var request = await store.Modules.CreateKitchenRequestAsync(
            new CreateKitchenRequestCommand(Guid.NewGuid(), [new QuantityInput(ChocolateCakeId, 3)]),
            submit: true);

        var waiting = Assert.Single(await store.Modules.GetKitchenRequestsAsync(), value => value.Id == request.Id);
        Assert.Equal(RequestDeliveryState.Waiting, waiting.DeliveryState);

        await using (var db = store.Database.CreateContext())
        {
            var message = await db.OutboxMessages.SingleAsync(value => value.AggregateId == request.Id && value.EventType == "kitchen_request.submitted");
            message.State = OutboxState.Acknowledged;
            message.AcknowledgedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        var sent = Assert.Single(await store.Modules.GetKitchenRequestsAsync(), value => value.Id == request.Id);
        Assert.Equal(RequestDeliveryState.Sent, sent.DeliveryState);
    }

    [Fact]
    public async Task ReceiveShipmentAsync_WhenEveryCountMatches_PostsIncomingExactlyOnce()
    {
        await using var store = await ModuleTestStore.CreateAsync();
        var shift = await store.Operations.OpenShiftAsync(ShiftKind.Morning, 0);
        var shipment = await CreateIncomingShipmentAsync(store.Database, store.Modules);
        var before = await ReadBalancesAsync(store.Database, shipment.Lines.Select(value => value.ItemId));
        var command = new ReceiveShipmentCommand(
            Guid.NewGuid(),
            shipment.Id,
            shipment.Version,
            shipment.Lines.Select(value => new ShipmentCountInput(value.Id, value.SentScaled)).ToArray());

        var result = await store.Modules.ReceiveShipmentAsync(command);
        var replay = await store.Modules.ReceiveShipmentAsync(command);

        Assert.Equal(IncomingReceiptStatus.Accepted, result.Status);
        Assert.False(result.EntireShipmentHeld);
        Assert.False(result.WasAlreadyCommitted);
        Assert.Equal(result.ReceiptId, replay.ReceiptId);
        Assert.True(replay.WasAlreadyCommitted);

        await using var db = store.Database.CreateContext();
        Assert.Empty(await db.StockHolds.ToListAsync());
        Assert.Empty(await db.QuantityConflicts.ToListAsync());
        Assert.Equal(shipment.Lines.Count, await db.StockMovements.CountAsync(
            value => value.DocumentId == result.ReceiptId && value.Kind == StockMovementKind.IncomingReceipt));
        Assert.Equal(1, await db.IncomingReceipts.CountAsync());
        foreach (var line in shipment.Lines)
        {
            var balance = await db.StockBalances.SingleAsync(value => value.ItemId == line.ItemId);
            Assert.Equal(before[line.ItemId] + line.SentScaled, balance.QuantityScaled);
            var snapshot = await db.ShiftItemSnapshots.SingleAsync(
                value => value.ShiftId == shift.Id && value.ItemId == line.ItemId);
            Assert.Equal(line.SentScaled, snapshot.IncomingScaled);
            Assert.Equal(balance.QuantityScaled, snapshot.ExpectedCloseScaled);
        }
        Assert.Single(await db.OutboxMessages.Where(value => value.EventType == "incoming_receipt.accepted").ToListAsync());
    }

    [Fact]
    public async Task ReceiveShipmentAsync_WhenOneCountDiffers_HoldsWholeShipmentWithoutSaleableStock()
    {
        await using var store = await ModuleTestStore.CreateAsync();
        var shift = await store.Operations.OpenShiftAsync(ShiftKind.Morning, 0);
        var shipment = await CreateIncomingShipmentAsync(store.Database, store.Modules);
        Assert.True(shipment.Lines.Count >= 2);
        var before = await ReadBalancesAsync(store.Database, shipment.Lines.Select(value => value.ItemId));
        var mismatchedLine = shipment.Lines[^1];
        var counts = shipment.Lines.Select(value => new ShipmentCountInput(
            value.Id,
            value.Id == mismatchedLine.Id ? value.SentScaled - 1 : value.SentScaled)).ToArray();
        var command = new ReceiveShipmentCommand(Guid.NewGuid(), shipment.Id, shipment.Version, counts);

        var result = await store.Modules.ReceiveShipmentAsync(command);
        var replay = await store.Modules.ReceiveShipmentAsync(command);

        Assert.Equal(IncomingReceiptStatus.Disputed, result.Status);
        Assert.True(result.EntireShipmentHeld);
        Assert.True(replay.WasAlreadyCommitted);
        await using var db = store.Database.CreateContext();
        Assert.Equal(shipment.Lines.Count, await db.StockHolds.CountAsync());
        Assert.Equal(shipment.Lines.Count, await db.ConflictLines.CountAsync());
        Assert.Empty(await db.StockMovements.Where(
            value => value.DocumentId == result.ReceiptId && value.Kind == StockMovementKind.IncomingReceipt).ToListAsync());
        Assert.All(await db.IncomingReceiptLines.ToListAsync(), value => Assert.Null(value.ConfirmedScaled));
        Assert.All(await db.StockHolds.ToListAsync(), value => Assert.Equal(HoldStatus.PendingDecision, value.Status));
        foreach (var line in shipment.Lines)
        {
            var balance = await db.StockBalances.SingleAsync(value => value.ItemId == line.ItemId);
            Assert.Equal(before[line.ItemId], balance.QuantityScaled);
            var snapshot = await db.ShiftItemSnapshots.SingleAsync(
                value => value.ShiftId == shift.Id && value.ItemId == line.ItemId);
            Assert.Equal(0, snapshot.IncomingScaled);
        }
        Assert.Single(await db.OutboxMessages.Where(value => value.EventType == "incoming_receipt.disputed").ToListAsync());
    }

    [Fact]
    public async Task PostManualIncomingAsync_PostsOnceAndQueuesVpsSyncEvent()
    {
        await using var store = await ModuleTestStore.CreateAsync();
        var shift = await store.Operations.OpenShiftAsync(ShiftKind.Morning, 0);
        var before = await ReadBalancesAsync(store.Database, [ChocolateCakeId]);
        var command = new PostManualIncomingCommand(Guid.NewGuid(), "توريد مباشر من المخزن", [new QuantityInput(ChocolateCakeId, 7)]);

        var first = await store.Modules.PostManualIncomingAsync(command);
        var replay = await store.Modules.PostManualIncomingAsync(command);

        Assert.False(first.WasAlreadyCommitted);
        Assert.True(replay.WasAlreadyCommitted);
        await using var db = store.Database.CreateContext();
        Assert.Equal(before[ChocolateCakeId] + 7, (await db.StockBalances.SingleAsync(x => x.ItemId == ChocolateCakeId)).QuantityScaled);
        Assert.Equal(7, (await db.ShiftItemSnapshots.SingleAsync(x => x.ShiftId == shift.Id && x.ItemId == ChocolateCakeId)).IncomingScaled);
        Assert.Single(await db.StockMovements.Where(x => x.DocumentId == command.CommandId && x.Kind == StockMovementKind.IncomingReceipt).ToListAsync());
        var message = Assert.Single(await db.OutboxMessages.Where(x => x.EventId == command.CommandId && x.EventType == "manual_incoming.posted").ToListAsync());
        Assert.Contains("توريد مباشر", message.PayloadJson);
    }

    [Fact]
    public async Task DispatchKitchenReturnAsync_ReplayDoesNotDeductStockTwice()
    {
        await using var store = await ModuleTestStore.CreateAsync();
        var shift = await store.Operations.OpenShiftAsync(ShiftKind.Evening, 0);
        var command = new DispatchKitchenReturnCommand(
            Guid.NewGuid(),
            "مرتجع جودة للمطبخ",
            [new QuantityInput(ChocolateCakeId, 2)]);

        var first = await store.Modules.DispatchKitchenReturnAsync(command);
        var replay = await store.Modules.DispatchKitchenReturnAsync(command);

        Assert.Equal(first.Id, replay.Id);
        Assert.True(replay.WasAlreadyCommitted);
        await using var db = store.Database.CreateContext();
        Assert.Equal(16, (await db.StockBalances.SingleAsync(value => value.ItemId == ChocolateCakeId)).QuantityScaled);
        var snapshot = await db.ShiftItemSnapshots.SingleAsync(
            value => value.ShiftId == shift.Id && value.ItemId == ChocolateCakeId);
        Assert.Equal(2, snapshot.KitchenReturnScaled);
        Assert.Equal(16, snapshot.ExpectedCloseScaled);
        var movement = await db.StockMovements.SingleAsync(
            value => value.DocumentId == first.Id && value.Kind == StockMovementKind.KitchenReturn);
        Assert.Equal(-2, movement.DeltaScaled);
        Assert.Single(await db.KitchenReturns.ToListAsync());
        Assert.Single(await db.OutboxMessages.Where(value => value.EventType == "kitchen_return.dispatched").ToListAsync());
    }

    [Fact]
    public async Task CorrectSaleAsync_PreservesOriginalCapsCumulativeRefundAndRestocksSeparately()
    {
        await using var store = await ModuleTestStore.CreateAsync();
        await store.Operations.OpenShiftAsync(ShiftKind.Morning, 10_000);
        var sale = await store.Operations.CompleteSaleAsync(CreateTwoItemSale(Guid.NewGuid()));
        var original = await store.Modules.GetSaleAsync(sale.Id);
        var gateaux = original.Lines.Single(value => value.ItemId == ChocolateGateauxId);
        var firstCommand = new CorrectSaleCommand(
            Guid.NewGuid(), sale.Id, gateaux.Id, 1, true, "مرتجع سليم", PaymentMethod.Cash, "test-user");

        var first = await store.Modules.CorrectSaleAsync(firstCommand);
        var replay = await store.Modules.CorrectSaleAsync(firstCommand);
        var second = await store.Modules.CorrectSaleAsync(new CorrectSaleCommand(
            Guid.NewGuid(), sale.Id, gateaux.Id, 3, false, "مرتجع تالف", PaymentMethod.Visa, "test-user"));
        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() => store.Modules.CorrectSaleAsync(
            new CorrectSaleCommand(Guid.NewGuid(), sale.Id, gateaux.Id, 1, true, "محاولة زائدة", PaymentMethod.Cash, "test-user")));

        Assert.True(replay.WasAlreadyCommitted);
        Assert.Equal(first.CorrectionId, replay.CorrectionId);
        Assert.Equal("REFUND_EXCEEDS_REMAINING", exception.Code);
        Assert.Equal(gateaux.EffectiveLineTotalMinor, first.RefundMinor + second.RefundMinor);

        var effective = await store.Modules.GetSaleAsync(sale.Id);
        var effectiveGateaux = effective.Lines.Single(value => value.Id == gateaux.Id);
        Assert.Equal(gateaux.OriginalQuantityScaled, effectiveGateaux.RefundedQuantityScaled);
        Assert.Equal(0, effectiveGateaux.RemainingRefundableScaled);
        Assert.Equal(0, effectiveGateaux.EffectiveLineTotalMinor);
        Assert.Equal(original.TipMinor, effective.TipMinor);

        await using var db = store.Database.CreateContext();
        var persistedSale = await db.Sales.Include(value => value.Lines).SingleAsync(value => value.Id == sale.Id);
        var persistedLine = persistedSale.Lines.Single(value => value.Id == gateaux.Id);
        Assert.Equal(4, persistedLine.QuantityScaled);
        Assert.Equal(gateaux.EffectiveLineTotalMinor, persistedLine.TotalMinor);
        Assert.Equal(sale.TotalMinor, persistedSale.TotalMinor);
        Assert.Equal(2, await db.SaleCorrections.CountAsync());
        Assert.Equal(2, await db.RefundPayments.CountAsync());
        Assert.Equal(-gateaux.EffectiveLineTotalMinor, await db.CashMovements
            .Where(value => value.Kind == "REFUND_CASH" || value.Kind == "REFUND_VISA")
            .SumAsync(value => value.AmountMinor));
        Assert.Equal(43, (await db.StockBalances.SingleAsync(value => value.ItemId == ChocolateGateauxId)).QuantityScaled);
        var restock = await db.StockMovements.SingleAsync(value => value.Kind == StockMovementKind.CustomerRestock);
        Assert.Equal(1, restock.DeltaScaled);
    }

    [Fact]
    public async Task ReceiptPrintJob_FailureAndRetryNeverCreateAnotherSale()
    {
        await using var store = await ModuleTestStore.CreateAsync();
        await store.Operations.OpenShiftAsync(ShiftKind.Morning, 0);
        var sale = await store.Operations.CompleteSaleAsync(CreateTwoItemSale(Guid.NewGuid()));

        var jobId = await store.Modules.GetOrResetPrintJobAsync(sale.Id, SideEffectKind.PrintReceipt);
        await store.Modules.MarkPrintFailedAsync(jobId, "synthetic printer offline");
        var retryJobId = await store.Modules.GetOrResetPrintJobAsync(sale.Id, SideEffectKind.PrintReceipt);
        await store.Modules.MarkPrintSucceededAsync(retryJobId);

        Assert.Equal(jobId, retryJobId);
        await using var db = store.Database.CreateContext();
        Assert.Single(await db.Sales.ToListAsync());
        var job = await db.SideEffectJobs.SingleAsync(value => value.Id == jobId);
        Assert.Equal(SideEffectState.Completed, job.State);
        Assert.Equal(1, job.Attempts);
        Assert.Null(job.LastError);
    }

    [Fact]
    public async Task CustomOrderDelivery_RequiresOpenShiftAndDoesNotDeductOnFailure()
    {
        await using var store = await ModuleTestStore.CreateAsync();
        var cafe = await store.Modules.CreateCafeProfileAsync(new CreateCafeProfileCommand(
            Guid.NewGuid(), "كافيه اختبار", "كافيه", "01234567890", "", null));
        var order = await store.Modules.CreateCustomOrderAsync(new CreateCustomOrderCommand(
            Guid.NewGuid(), cafe.Id, "طلب اختبار", DateTimeOffset.UtcNow.AddDays(1),
            [new CafeOrderLineInput(ChocolateCakeId, 1)]));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            store.Modules.ChangeCustomOrderStatusAsync(new ChangeCustomOrderStatusCommand(
                Guid.NewGuid(), order.Id, order.Version, CustomOrderStatus.Delivered)));
        Assert.Equal("SHIFT_REQUIRED", error.Code);

        await using var db = store.Database.CreateContext();
        Assert.Equal(18, (await db.StockBalances.SingleAsync(value => value.ItemId == ChocolateCakeId)).QuantityScaled);
        Assert.Empty(await db.StockMovements.Where(value => value.DocumentId == order.Id).ToListAsync());
    }

    [Fact]
    public async Task CustomOrders_ShareCustomerBalanceAndNeverAffectShiftCash()
    {
        await using var store = await ModuleTestStore.CreateAsync();
        var cafe = await store.Modules.CreateCafeProfileAsync(new CreateCafeProfileCommand(
            Guid.NewGuid(), "كافيه النيل", "كافيه", "01000000000", "الزمالك", null));
        var cafeDetails = await store.Modules.GetCafeProfileAsync(cafe.Id);
        await store.Modules.SaveCafePriceListAsync(new SaveCafePriceListCommand(
            Guid.NewGuid(), cafe.Id, cafe.Version,
            cafeDetails.Prices.Select(value => new SaveCafePriceInput(
                value.ItemId, value.ItemId == ChocolateCakeId ? 20_000 : value.UnitPriceMinor)).ToArray()));

        var supermarket = await store.Modules.CreateCafeProfileAsync(new CreateCafeProfileCommand(
            Guid.NewGuid(), "سوبر ماركت النيل", "سوبر ماركت", "01111111111", "الدقي", cafe.Id));
        var supermarketDetails = await store.Modules.GetCafeProfileAsync(supermarket.Id);
        Assert.Equal(20_000, supermarketDetails.Prices.Single(value => value.ItemId == ChocolateCakeId).UnitPriceMinor);
        await store.Modules.SaveCafePriceListAsync(new SaveCafePriceListCommand(
            Guid.NewGuid(), supermarket.Id, supermarket.Version,
            supermarketDetails.Prices.Select(value => new SaveCafePriceInput(
                value.ItemId, value.ItemId == ChocolateCakeId ? 18_000 : value.UnitPriceMinor)).ToArray()));
        Assert.Equal(20_000, (await store.Modules.GetCafeProfileAsync(cafe.Id)).Prices.Single(value => value.ItemId == ChocolateCakeId).UnitPriceMinor);

        var createId = Guid.NewGuid();
        var command = new CreateCustomOrderCommand(
            createId,
            cafe.Id,
            "تورتة عيد ميلاد باسم سارة",
            DateTimeOffset.UtcNow.AddDays(3),
            [new CafeOrderLineInput(ChocolateCakeId, 1)]);

        var created = await store.Modules.CreateCustomOrderAsync(command);
        var replay = await store.Modules.CreateCustomOrderAsync(command);
        await store.Modules.CreateCustomOrderAsync(new CreateCustomOrderCommand(
            Guid.NewGuid(), cafe.Id, "بوكس حلويات للشركة", DateTimeOffset.UtcNow.AddDays(4),
            [new CafeOrderLineInput(ChocolateCakeId, 2)]));
        var paid = await store.Modules.AddCustomOrderPaymentAsync(new AddCustomOrderPaymentCommand(
            Guid.NewGuid(), created.Id, created.Version, 15_000, PaymentMethod.Cash));
        await store.Operations.OpenShiftAsync(ShiftKind.Morning, 10_000);
        var delivered = await store.Modules.ChangeCustomOrderStatusAsync(new ChangeCustomOrderStatusCommand(
            Guid.NewGuid(), paid.Id, paid.Version, CustomOrderStatus.Delivered));

        Assert.True(replay.WasAlreadyCommitted);
        Assert.Equal(created.Id, replay.Id);
        Assert.Equal(15_000, delivered.PaidMinor);
        Assert.Equal(5_000, delivered.RemainingMinor);
        Assert.Equal(45_000, delivered.CustomerBalanceMinor);
        Assert.Equal(CustomOrderStatus.Delivered, delivered.Status);
        var customerOrders = await store.Modules.GetCustomOrdersAsync();
        Assert.Equal(2, customerOrders.Count);
        Assert.Equal(5_000, customerOrders.Single(value => value.Id == delivered.Id).RemainingMinor);
        Assert.Equal(40_000, customerOrders.Single(value => value.Id != delivered.Id).RemainingMinor);

        var closing = await store.Modules.GetClosingPreviewAsync();
        Assert.Equal(10_000, closing.ExpectedCashMinor);
        Assert.Equal(1, closing.Items.Single(value => value.ItemId == ChocolateCakeId).CafeIssuedScaled);

        await using var db = store.Database.CreateContext();
        Assert.Equal(2, await db.CustomOrders.CountAsync());
        Assert.Equal(2, await db.CustomOrderLines.CountAsync());
        Assert.Equal(17, (await db.StockBalances.SingleAsync(value => value.ItemId == ChocolateCakeId)).QuantityScaled);
        Assert.Single(await db.StockMovements.Where(value => value.DocumentId == delivered.Id && value.Kind == StockMovementKind.CafeIssue).ToListAsync());
        var payment = Assert.Single(await db.CustomOrderPayments.ToListAsync());
        Assert.Null(payment.ShiftId);
        Assert.Equal(3, await db.CustomOrderActivities.CountAsync(value => value.CustomOrderId == created.Id));
        Assert.Equal(4, await db.OutboxMessages.CountAsync(value => value.AggregateId == created.Id));
        Assert.Empty(await db.CashMovements.Where(value => value.Kind.StartsWith("CUSTOM_ORDER")).ToListAsync());
        Assert.Equal(2, await db.SideEffectJobs.CountAsync(value => value.SourceId == created.Id && value.Kind == SideEffectKind.PrintCustomOrder));
    }

    [Fact]
    public async Task CloseShift_WithNoCatalogItems_ClosesAndQueuesEmptyReport()
    {
        await using var store = await ModuleTestStore.CreateAsync();
        await using (var db = store.Database.CreateContext())
        {
            foreach (var item in await db.CatalogItems.ToListAsync()) item.Active = false;
            foreach (var balance in await db.StockBalances.ToListAsync()) balance.QuantityScaled = 0;
            await db.SaveChangesAsync();
        }

        var shift = await store.Operations.OpenShiftAsync(ShiftKind.Morning, 2_500);
        var preview = await store.Modules.GetClosingPreviewAsync();

        Assert.Empty(preview.Items);
        var closed = await store.Modules.CloseShiftAsync(new CloseShiftCommand(Guid.NewGuid(), [], 2_500));

        Assert.Empty(closed.Report.Items);
        await using var verify = store.Database.CreateContext();
        Assert.Equal(ShiftStatus.Closed, (await verify.Shifts.SingleAsync(value => value.Id == shift.Id)).Status);
        Assert.Empty(await verify.StockCountLines.ToListAsync());
        Assert.Single(await verify.OutboxMessages.Where(value => value.EventType == "shift.closed").ToListAsync());
        Assert.Equal(3, await verify.SideEffectJobs.CountAsync(value => value.SourceId == shift.Id));
    }

    [Fact]
    public async Task CloseShift_RejectsInconsistentStockWithoutClosingOrLosingOutbox()
    {
        await using var store = await ModuleTestStore.CreateAsync();
        var shift = await store.Operations.OpenShiftAsync(ShiftKind.Morning, 0);
        await using (var db = store.Database.CreateContext())
        {
            var row = await db.ShiftItemSnapshots.SingleAsync(value => value.ShiftId == shift.Id && value.ItemId == ChocolateCakeId);
            row.ExpectedCloseScaled = -1;
            await db.SaveChangesAsync();
        }

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => store.Modules.GetClosingPreviewAsync());
        Assert.Equal("STOCK_RECONCILIATION_FAILED", error.Code);
        await using var verify = store.Database.CreateContext();
        Assert.Equal(ShiftStatus.Open, (await verify.Shifts.SingleAsync(value => value.Id == shift.Id)).Status);
        Assert.True(await verify.OutboxMessages.AnyAsync(value => value.AggregateId == shift.Id));
    }

    [Fact]
    public async Task CloseShiftAndXlsxReport_AreImmutableRetryableAndDoNotAdjustCountDifferences()
    {
        await using var store = await ModuleTestStore.CreateAsync();
        var shift = await store.Operations.OpenShiftAsync(ShiftKind.Morning, 10_000);
        await store.Operations.CompleteSaleAsync(CreateTwoItemSale(Guid.NewGuid()));
        var preview = await store.Modules.GetClosingPreviewAsync();
        var cake = preview.Items.Single(value => value.ItemId == ChocolateCakeId);
        var closeCommand = new CloseShiftCommand(
            Guid.NewGuid(),
            preview.Items.Select(value => new ClosingCountInput(
                value.ItemId,
                value.ItemId == ChocolateCakeId ? value.ExpectedScaled - 1 : value.ExpectedScaled)).ToArray(),
            preview.ExpectedCashMinor - 100);

        var closed = await store.Modules.CloseShiftAsync(closeCommand);
        var replay = await store.Modules.CloseShiftAsync(closeCommand);

        Assert.False(closed.WasAlreadyCommitted);
        Assert.True(replay.WasAlreadyCommitted);
        Assert.Equal(closed.ExportJobId, replay.ExportJobId);
        Assert.Equal(-100, closed.Report.DifferenceMinor);
        Assert.Equal(-1, closed.Report.Items.Single(value => value.Name == cake.ItemName).DifferenceScaled);

        await using (var db = store.Database.CreateContext())
        {
            Assert.Equal(ShiftStatus.Closed, (await db.Shifts.SingleAsync(value => value.Id == shift.Id)).Status);
            Assert.Equal(17, (await db.StockBalances.SingleAsync(value => value.ItemId == ChocolateCakeId)).QuantityScaled);
            Assert.Single(await db.AdjustmentRequests.ToListAsync());
            Assert.Equal(AdjustmentRequestStatus.PendingAdmin, (await db.AdjustmentRequests.SingleAsync()).Status);
            Assert.Equal(3, await db.SideEffectJobs.CountAsync(value => value.SourceId == shift.Id));
            Assert.Single(await db.OutboxMessages.Where(value => value.EventType == "shift.closed").ToListAsync());
            Assert.Empty(await db.ReportArtifacts.ToListAsync());
        }

        var writer = new OpenXmlShiftReportWriter();
        var written = await writer.WriteAsync(closed.Report, store.ExportDirectory);
        var secondWrite = await writer.WriteAsync(closed.Report, store.ExportDirectory);
        Assert.True(File.Exists(written.Path));
        Assert.Equal(".xlsx", Path.GetExtension(written.Path));
        Assert.True(written.ByteLength > 0);
        Assert.Matches("^[0-9a-f]{64}$", written.Sha256);
        Assert.True(secondWrite.ExistingIdenticalFile);
        Assert.Equal(written.Sha256, secondWrite.Sha256);
        Assert.Equal(written.ByteLength, secondWrite.ByteLength);

        var previewDirectory = Environment.GetEnvironmentVariable("SUGAR_BRANCH1_REPORT_PREVIEW_DIR");
        if (!string.IsNullOrWhiteSpace(previewDirectory))
        {
            Directory.CreateDirectory(previewDirectory);
            File.Copy(written.Path, Path.Combine(previewDirectory, "branch-type-1-shift-report.xlsx"), overwrite: true);
        }

        using (var document = SpreadsheetDocument.Open(written.Path, false))
        {
            var workbookPart = Assert.IsType<WorkbookPart>(document.WorkbookPart);
            var workbook = Assert.IsType<Workbook>(workbookPart.Workbook);
            var sheetCollection = Assert.IsType<Sheets>(workbook.GetFirstChild<Sheets>());
            var sheets = sheetCollection.Elements<Sheet>().ToArray();
            Assert.Equal(
                new[] { "تقرير الوردية", "_metadata" },
                sheets.Select(value => value.Name?.Value ?? string.Empty).ToArray());
            Assert.Equal(SheetStateValues.VeryHidden, sheets[^1].State!.Value);
            var reportPart = Assert.IsType<WorksheetPart>(workbookPart.GetPartById(sheets[0].Id!.Value!));
            var reportWorksheet = Assert.IsType<Worksheet>(reportPart.Worksheet);
            var visibleText = string.Join(
                " | ",
                reportWorksheet.Descendants<Cell>()
                    .Select(value => value.InlineString?.InnerText)
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            Assert.Contains("صافي المبيعات", visibleText);
            Assert.Contains("حركة الأصناف", visibleText);
            Assert.Contains("فروق تحتاج مراجعة", visibleText);
            Assert.DoesNotContain("مرتجعات نقدي", visibleText);
            Assert.DoesNotContain("مرتجعات فيزا", visibleText);
            Assert.DoesNotContain("عد فعلي", visibleText);
            var validationErrors = new OpenXmlValidator().Validate(document).ToArray();
            Assert.True(
                validationErrors.Length == 0,
                string.Join(Environment.NewLine, validationErrors.Select(value => $"{value.Id}: {value.Description} ({value.Path?.XPath})")));
        }

        var changedReport = closed.Report with
        {
            ActualCashMinor = closed.Report.ActualCashMinor + 1,
            DifferenceMinor = closed.Report.DifferenceMinor + 1
        };
        var conflict = await Assert.ThrowsAsync<BusinessRuleException>(
            () => writer.WriteAsync(changedReport, store.ExportDirectory));
        Assert.Equal("IMMUTABLE_REPORT_CONFLICT", conflict.Code);

        await store.Modules.MarkReportSucceededAsync(closed.ExportJobId, written);
        await store.Modules.MarkReportSucceededAsync(closed.ExportJobId, written);
        var history = await store.Modules.GetClosedShiftsAsync();
        var historyShift = Assert.Single(history);
        Assert.Equal("جاهز", historyShift.ReportStatus);
        Assert.Equal(written.Path, historyShift.ReportPath);
        Assert.Equal(written.Sha256, historyShift.ReportHash);
    }

    private static async Task<IncomingShipmentSnapshot> CreateIncomingShipmentAsync(LocalDatabase database, BranchModuleOperationsService modules)
    {
        var requestSnapshot = await modules.CreateKitchenRequestAsync(
            new CreateKitchenRequestCommand(
                Guid.NewGuid(),
                [new QuantityInput(ChocolateCakeId, 3), new QuantityInput(ChocolateGateauxId, 5)]),
            submit: true);
        await using var db = database.CreateContext();
        var request = await db.KitchenRequests.Include(value => value.Lines).SingleAsync(value => value.Id == requestSnapshot.Id);
        var shipment = new Shipment
        {
            Id = Guid.NewGuid(),
            CommandId = Guid.NewGuid(),
            RequestId = request.Id,
            Reference = "TEST-INCOMING-001",
            Status = ShipmentStatus.AwaitingReceipt,
            Version = 1,
            DispatchedAtUtc = DateTimeOffset.UtcNow
        };
        var requestLines = request.Lines.OrderBy(value => value.ItemId).ToArray();
        for (var index = 0; index < requestLines.Length; index += 1)
        {
            var requestLine = requestLines[index];
            var sent = index == 1 ? Math.Min(2L * requestLine.QuantityScale, requestLine.RequestedScaled) : requestLine.RequestedScaled;
            shipment.Lines.Add(new ShipmentLine
            {
                Id = Guid.NewGuid(),
                ShipmentId = shipment.Id,
                RequestLineId = requestLine.Id,
                ItemId = requestLine.ItemId,
                NameSnapshot = requestLine.NameSnapshot,
                UnitSnapshot = requestLine.UnitSnapshot,
                QuantityScale = requestLine.QuantityScale,
                SentScaled = sent
            });
            requestLine.ApprovedScaled = requestLine.RequestedScaled;
            requestLine.SentScaled = sent;
        }
        request.Status = KitchenRequestStatus.Partial;
        request.Version += 1;
        db.Shipments.Add(shipment);
        await db.SaveChangesAsync();
        return (await modules.GetIncomingShipmentsAsync()).Single(value => value.Id == shipment.Id);
    }

    private static async Task<Dictionary<Guid, long>> ReadBalancesAsync(LocalDatabase database, IEnumerable<Guid> itemIds)
    {
        var ids = itemIds.Distinct().ToArray();
        await using var db = database.CreateContext();
        return await db.StockBalances.Where(value => ids.Contains(value.ItemId))
            .ToDictionaryAsync(value => value.ItemId, value => value.QuantityScaled);
    }

    private static CompleteSaleCommand CreateTwoItemSale(Guid commandId) => new(
        commandId,
        [new SaleCartLine(ChocolateCakeId, 1), new SaleCartLine(ChocolateGateauxId, 4)],
        PaymentMethod.Cash,
        FulfillmentKind.Takeaway,
        DiscountMinor: 9_000,
        TipMinor: 1_000);

    private sealed class ModuleTestStore : IAsyncDisposable
    {
        private readonly string _directory;

        private ModuleTestStore(string directory)
        {
            _directory = directory;
            ExportDirectory = Path.Combine(directory, "exports");
            Database = new LocalDatabase(Path.Combine(directory, "branch-type-1.db"));
            Operations = new BranchOperationsService(Database);
            Modules = new BranchModuleOperationsService(Database);
        }

        public string ExportDirectory { get; }
        public LocalDatabase Database { get; }
        public BranchOperationsService Operations { get; }
        public BranchModuleOperationsService Modules { get; }

        public static async Task<ModuleTestStore> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "sugar-erp-branch1-module-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var store = new ModuleTestStore(directory);
            await store.Operations.InitializeAsync();
            await BranchTestFixtureSeeder.SeedAsync(store.Database);
            return store;
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
            return ValueTask.CompletedTask;
        }
    }
}
