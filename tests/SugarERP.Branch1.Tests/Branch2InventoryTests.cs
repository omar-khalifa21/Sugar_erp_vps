using Microsoft.EntityFrameworkCore;
using SugarERP.Application;
using SugarERP.Domain;
using SugarERP.Infrastructure.Local;
using Xunit;

namespace SugarERP.Branch1.Tests;

public sealed class Branch2InventoryTests
{
    [Fact]
    public async Task ActualCafeDeliveryAndKitchenReturnUseStockAndReplayOnlyOnce()
    {
        var (database, item, user) = await Create();
        await using (var db = database.CreateContext())
        {
            db.StockBalances.Add(new StockBalance { ItemId = item, QuantityScaled = 99, Revision = 1 });
            db.LocationBalances.Add(new BranchLocationBalance { ItemId = item, Location = BranchInventoryLocation.Stock, QuantityScaled = 5 });
            db.LocationBalances.Add(new BranchLocationBalance { ItemId = item, Location = BranchInventoryLocation.Display, QuantityScaled = 3 });
            await db.SaveChangesAsync();
        }
        await new BranchOperationsService(database).OpenShiftAsync(ShiftKind.Morning, 0);
        var modules = new BranchModuleOperationsService(database);
        var cafe = await modules.CreateCafeProfileAsync(new CreateCafeProfileCommand(Guid.NewGuid(), "Cafe test", "Cafe", "01000000000", "Test address", null));
        var order = await modules.CreateCustomOrderAsync(new CreateCustomOrderCommand(Guid.NewGuid(), cafe.Id, "Delivery test", DateTimeOffset.UtcNow.AddDays(1), [new CafeOrderLineInput(item, 1)]));
        var delivery = new ChangeCustomOrderStatusCommand(Guid.NewGuid(), order.Id, order.Version, CustomOrderStatus.Delivered, user, "signed-fixture-authorization");
        await modules.ChangeCustomOrderStatusAsync(delivery);
        await modules.ChangeCustomOrderStatusAsync(delivery);
        var kitchenReturn = new DispatchKitchenReturnCommand(Guid.NewGuid(), "Damaged packaging", [new QuantityInput(item, 1)], user, "signed-fixture-authorization");
        await modules.DispatchKitchenReturnAsync(kitchenReturn);
        await modules.DispatchKitchenReturnAsync(kitchenReturn);
        await using var final = database.CreateContext();
        Assert.Equal(3, (await final.LocationBalances.FindAsync(item, BranchInventoryLocation.Stock))!.QuantityScaled);
        Assert.Equal(3, (await final.LocationBalances.FindAsync(item, BranchInventoryLocation.Display))!.QuantityScaled);
        Assert.Equal(99, (await final.StockBalances.FindAsync(item))!.QuantityScaled);
        Assert.Equal(2, await final.InventoryTransactions.CountAsync());
        Assert.Equal(2, await final.OutboxMessages.CountAsync(x => x.EventType == "branch2.inventory.posted"));
        Assert.Single(await final.KitchenReturns.ToListAsync());
        Assert.Single(await final.StockMovements.Where(x => x.Kind == StockMovementKind.CafeIssue).ToListAsync());
        var shiftSnapshot = await final.ShiftItemSnapshots.SingleAsync(x => x.ItemId == item);
        Assert.Equal(8, shiftSnapshot.OpeningQuantityScaled);
        Assert.Equal(6, shiftSnapshot.ExpectedCloseScaled);
        var closeCommand = new CloseShiftCommand(Guid.NewGuid(), [new ClosingCountInput(item, 6)], 0, user, "signed-fixture-authorization");
        var closed = await modules.CloseShiftAsync(closeCommand);
        var replayClosed = await modules.CloseShiftAsync(closeCommand);
        Assert.False(closed.WasAlreadyCommitted);
        Assert.True(replayClosed.WasAlreadyCommitted);
        await using var afterClose = database.CreateContext();
        Assert.Equal(6, (await afterClose.LocationBalances.FindAsync(item, BranchInventoryLocation.Stock))!.QuantityScaled);
        Assert.Equal(0, (await afterClose.LocationBalances.FindAsync(item, BranchInventoryLocation.Display))!.QuantityScaled);
        Assert.Equal(ShiftStatus.Closed, (await afterClose.Shifts.SingleAsync()).Status);
        Assert.Equal(3, await afterClose.InventoryTransactions.CountAsync());
        Assert.Single(await afterClose.InventoryTransactions.Where(x => x.Kind == Branch2TransactionKind.DisplayReturnToStock).ToListAsync());
    }

    [Fact]
    public async Task BusinessExampleConservesStockAndEveryRetryIsIdempotent()
    {
        var (database, item, user) = await Create();
        var service = new Branch2InventoryService(database);
        async Task Post(Branch2TransactionKind kind, long count)
        {
            var reference = kind == Branch2TransactionKind.IncomingReceipt ? await SeedReceipt(database, item, count) : Guid.NewGuid();
            var command = new Branch2InventoryCommand(Guid.NewGuid(), reference, user, kind,
                count == 0 ? new Dictionary<Guid, long>() : new Dictionary<Guid, long> { [item] = count }, "Business test", "signed-fixture-authorization");
            var first = await service.ExecuteAsync(command);
            for (var i = 0; i < 10; i++) Assert.Equal(first.Id, (await service.ExecuteAsync(command)).Id);
        }
        await Post(Branch2TransactionKind.IncomingReceipt, 10);
        await Post(Branch2TransactionKind.StockToDisplay, 6);
        await Post(Branch2TransactionKind.RetailSale, 2);
        await Post(Branch2TransactionKind.CafeIssue, 1);
        await Post(Branch2TransactionKind.KitchenReturn, 1);
        await using (var db = database.CreateContext())
        {
            Assert.Equal(2, (await db.LocationBalances.FindAsync(item, BranchInventoryLocation.Stock))!.QuantityScaled);
            Assert.Equal(4, (await db.LocationBalances.FindAsync(item, BranchInventoryLocation.Display))!.QuantityScaled);
        }
        await Post(Branch2TransactionKind.DisplayReturnToStock, 0);
        await using var final = database.CreateContext();
        Assert.Equal(6, (await final.LocationBalances.FindAsync(item, BranchInventoryLocation.Stock))!.QuantityScaled);
        Assert.Equal(0, (await final.LocationBalances.FindAsync(item, BranchInventoryLocation.Display))!.QuantityScaled);
        Assert.Equal(6, await final.InventoryTransactions.CountAsync());
        Assert.Equal(6, await final.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task InsufficientStockRollsBackBalancesLedgerAndOutbox()
    {
        var (database, item, user) = await Create();
        var service = new Branch2InventoryService(database);
        foreach (var kind in new[] { Branch2TransactionKind.StockToDisplay, Branch2TransactionKind.RetailSale, Branch2TransactionKind.CafeIssue, Branch2TransactionKind.KitchenReturn })
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.ExecuteAsync(new(Guid.NewGuid(), Guid.NewGuid(), user, kind,
                new Dictionary<Guid, long> { [item] = 1 }, "Insufficient test", "signed-fixture-authorization")));
        await using var db = database.CreateContext();
        Assert.Empty(await db.LocationBalances.ToListAsync());
        Assert.Empty(await db.InventoryTransactions.ToListAsync());
        Assert.Empty(await db.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentLastItemSalesCannotMakeDisplayNegative()
    {
        var (database, item, user) = await Create();
        var service = new Branch2InventoryService(database);
        await service.ExecuteAsync(new(Guid.NewGuid(), await SeedReceipt(database, item, 1), user, Branch2TransactionKind.IncomingReceipt, new Dictionary<Guid, long> { [item] = 1 }, "Receive", "signed-fixture-authorization"));
        await service.ExecuteAsync(new(Guid.NewGuid(), Guid.NewGuid(), user, Branch2TransactionKind.StockToDisplay, new Dictionary<Guid, long> { [item] = 1 }, "Display", "signed-fixture-authorization"));
        async Task<bool> Sale() { try { await service.ExecuteAsync(new(Guid.NewGuid(), Guid.NewGuid(), user, Branch2TransactionKind.RetailSale, new Dictionary<Guid, long> { [item] = 1 }, "Sale", "signed-fixture-authorization")); return true; } catch (BusinessRuleException) { return false; } }
        var results = await Task.WhenAll(Sale(), Sale());
        Assert.Single(results, x => x);
        await using var db = database.CreateContext();
        Assert.Equal(0, (await db.LocationBalances.FindAsync(item, BranchInventoryLocation.Display))!.QuantityScaled);
    }

    private static async Task<(LocalDatabase Database, Guid Item, Guid User)> Create()
    {
        var database = new LocalDatabase(Path.Combine(Path.GetTempPath(), "SugarERP-Branch2-tests", Guid.NewGuid().ToString(), "branch.db"));
        await database.InitializeAsync();
        var item = Guid.NewGuid();
        await using var db = database.CreateContext();
        db.DeviceConfigurations.Add(new DeviceConfiguration { SiteId = Guid.NewGuid(), DeviceId = Guid.NewGuid(), Profile = DeviceProfile.BranchType2, SiteName = "Branch2 test" });
        db.CatalogItems.Add(new CatalogItem { Id = item, Sku = "TEST", NameAr = "Cake", Unit = "pcs" });
        db.SequenceStates.Add(new SequenceState());
        await db.SaveChangesAsync();
        return (database, item, Guid.NewGuid());
    }

    private static async Task<Guid> SeedReceipt(LocalDatabase database, Guid item, long quantity)
    {
        await using var db = database.CreateContext();
        var shipment = new Shipment { Id = Guid.NewGuid(), CommandId = Guid.NewGuid(), Reference = Guid.NewGuid().ToString(), Status = ShipmentStatus.Received, DispatchedAtUtc = DateTimeOffset.UtcNow };
        var line = new ShipmentLine { Id = Guid.NewGuid(), ItemId = item, SentScaled = quantity, QuantityScale = 1, NameSnapshot = "Cake", UnitSnapshot = "pcs" };
        shipment.Lines.Add(line);
        var receipt = new IncomingReceipt { Id = Guid.NewGuid(), CommandId = Guid.NewGuid(), ShipmentId = shipment.Id, Status = IncomingReceiptStatus.Accepted, CountedAtUtc = DateTimeOffset.UtcNow };
        receipt.Lines.Add(new IncomingReceiptLine { Id = Guid.NewGuid(), ItemId = item, ShipmentLineId = line.Id, SentScaled = quantity, CountedScaled = quantity, ConfirmedScaled = quantity });
        db.Shipments.Add(shipment); db.IncomingReceipts.Add(receipt); await db.SaveChangesAsync();
        return receipt.Id;
    }
}
