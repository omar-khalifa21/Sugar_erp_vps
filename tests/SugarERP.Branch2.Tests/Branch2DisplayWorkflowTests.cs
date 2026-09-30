using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SugarERP.Application;
using SugarERP.Domain;
using SugarERP.Infrastructure.Local;
using Xunit;

namespace SugarERP.Branch2.Tests;

public sealed class Branch2DisplayWorkflowTests
{
    [Fact]
    public async Task DisplayTransferAndSale_ConserveStockAndSellOnlyFromDisplay()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sugar-erp-branch2-display-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var database = new LocalDatabase(Path.Combine(directory, "branch.db"));
        try
        {
            await database.InitializeAsync();
            var itemId = Guid.NewGuid();
            var deviceId = Guid.NewGuid();
            await using (var setup = database.CreateContext())
            {
                setup.DeviceConfigurations.Add(new DeviceConfiguration
                {
                    SiteId = Guid.NewGuid(),
                    DeviceId = deviceId,
                    Profile = DeviceProfile.BranchType2,
                    SiteName = "اختبار الفرع ٢"
                });
                setup.SequenceStates.Add(new SequenceState());
                setup.CatalogItems.Add(new CatalogItem
                {
                    Id = itemId,
                    Sku = "CAKE",
                    NameAr = "كيكة شوكولاتة",
                    Unit = "قطعة",
                    QuantityScale = 1,
                    RetailPriceMinor = 7_000
                });
                setup.StockBalances.Add(new StockBalance { ItemId = itemId, QuantityScaled = 20, Revision = 1 });
                setup.LocationBalances.Add(new BranchLocationBalance
                {
                    ItemId = itemId,
                    Location = BranchInventoryLocation.Stock,
                    QuantityScaled = 20
                });
                await setup.SaveChangesAsync();
            }

            var operations = new BranchOperationsService(database);
            var before = Assert.Single(await operations.GetDisplayStockAsync());
            Assert.Equal(20, before.StockQuantityScaled);
            Assert.Equal(0, before.DisplayQuantityScaled);

            await operations.MoveStockToDisplayAsync(new MoveStockToDisplayCommand(
                Guid.NewGuid(), itemId, 5, "تجهيز العرض"));
            await operations.OpenShiftAsync(ShiftKind.Morning, 0);
            await operations.CompleteSaleAsync(new CompleteSaleCommand(
                Guid.NewGuid(),
                [new SaleCartLine(itemId, 2)],
                PaymentMethod.Cash,
                FulfillmentKind.Takeaway,
                0,
                0,
                deviceId,
                $"device-writer:{deviceId:N}"));

            var after = Assert.Single(await operations.GetDisplayStockAsync());
            Assert.Equal(15, after.StockQuantityScaled);
            Assert.Equal(3, after.DisplayQuantityScaled);
            Assert.Equal(18, after.StockQuantityScaled + after.DisplayQuantityScaled);

            await using var verify = database.CreateContext();
            Assert.Single(await verify.InventoryTransactions.Where(value => value.Kind == Branch2TransactionKind.StockToDisplay).ToListAsync());
            Assert.Single(await verify.InventoryTransactions.Where(value => value.Kind == Branch2TransactionKind.RetailSale).ToListAsync());
            Assert.Equal(2, await verify.OutboxMessages.CountAsync(value => value.EventType == "branch2.inventory.posted"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
