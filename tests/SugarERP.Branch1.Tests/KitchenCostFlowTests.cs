using Microsoft.EntityFrameworkCore;
using SugarERP.Application;
using SugarERP.Infrastructure.Local;
using SugarERP.Kitchen;
using SugarERP.Sync.Client;
using Xunit;

namespace SugarERP.Branch1.Tests;

public sealed class KitchenCostFlowTests
{
    [Fact]
    public async Task PurchasesUseWeightedAverageAndDispatchDeductsRecipeOnce()
    {
        var (store, sync) = await SetupAsync();
        var flour = await sync.SaveCatalogItemAsync(null, null, "Flour", "g", 1, "INGREDIENT");
        var eggs = await sync.SaveCatalogItemAsync(null, null, "Eggs", "قطعة", 1, "INGREDIENT");
        var cake = await sync.SaveCatalogItemAsync(null, null, "Cake", "قطعة", 1, "PRODUCT", 50_000);
        await sync.ReceiveIngredientPurchaseAsync(flour.Id, 1_000, 50_000, "Flour purchase");
        await sync.ReceiveIngredientPurchaseAsync(eggs.Id, 30, 18_000, "Egg purchase");
        await sync.SaveRecipeAsync(cake.Id, 0, 1, new Dictionary<Guid, long> { [flour.Id] = 500, [eggs.Id] = 4 });

        var requestId = Guid.NewGuid();
        await using (var db = store.Open())
        {
            db.Requests.Add(new KitchenRequestRecord { Id = requestId, BranchSiteId = Guid.NewGuid(), BranchName = "Test Branch",
                Status = "RECEIVED", Version = 1, SubmittedAtUtc = DateTimeOffset.UtcNow,
                Lines = [new KitchenRequestLineRecord { Id = Guid.NewGuid(), ItemId = cake.Id, Name = cake.Name,
                    Unit = cake.Unit, QuantityScale = 1, RequestedScaled = 1 }] });
            await db.SaveChangesAsync();
        }
        Guid lineId;
        await using (var db = store.Open()) lineId = await db.Requests.Where(x => x.Id == requestId)
            .SelectMany(x => x.Lines).Select(x => x.Id).SingleAsync();
        await sync.DispatchAsync(requestId, new Dictionary<Guid, long> { [lineId] = 1 });
        await Assert.ThrowsAsync<InvalidOperationException>(() => sync.DispatchAsync(requestId,
            new Dictionary<Guid, long> { [lineId] = 1 }));

        await using var verify = store.Open();
        var flourBalance = await verify.Ingredients.SingleAsync(x => x.ItemId == flour.Id);
        var eggBalance = await verify.Ingredients.SingleAsync(x => x.ItemId == eggs.Id);
        Assert.Equal(500, flourBalance.QuantityScaled);
        Assert.Equal(25_000, flourBalance.InventoryCostMinor);
        Assert.Equal(26, eggBalance.QuantityScaled);
        Assert.Equal(15_600, eggBalance.InventoryCostMinor);
        var dispatch = await verify.IngredientMovements.Where(x => x.Kind == "DISPATCH").ToArrayAsync();
        Assert.Equal(2, dispatch.Length);
        Assert.Equal(27_400, dispatch.Sum(x => x.CostMinor));
    }

    [Fact]
    public async Task CafeFulfillmentDeductsIngredientsAndCannotRunTwice()
    {
        var (store, sync) = await SetupAsync();
        var eggs = await sync.SaveCatalogItemAsync(null, null, "Eggs", "قطعة", 1, "INGREDIENT");
        var cake = await sync.SaveCatalogItemAsync(null, null, "Cake", "قطعة", 1, "PRODUCT", 50_000);
        await sync.ReceiveIngredientPurchaseAsync(eggs.Id, 30, 18_000, "Egg purchase");
        await sync.SaveRecipeAsync(cake.Id, 0, 1, new Dictionary<Guid, long> { [eggs.Id] = 4 });
        var cafe = await sync.SaveCafeAsync(null, null, "Cafe A", "01000000000");
        var order = await sync.CreateCustomOrderAsync(cafe.Id, DateTimeOffset.UtcNow.AddDays(1), "",
            new Dictionary<Guid, long> { [cake.Id] = 2 });
        await sync.DeliverCafeOrderAsync(order.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sync.DeliverCafeOrderAsync(order.Id));
        await using var db = store.Open();
        var balance = await db.Ingredients.SingleAsync(x => x.ItemId == eggs.Id);
        Assert.Equal(22, balance.QuantityScaled);
        Assert.Equal(13_200, balance.InventoryCostMinor);
        var transaction = await db.IngredientMovements.SingleAsync(x => x.Kind == "CAFE_PRODUCTION");
        Assert.Equal(4_800, transaction.CostMinor);
        Assert.Equal("DELIVERED", (await db.CustomOrders.SingleAsync()).Status);
    }

    private static async Task<(KitchenStore Store, KitchenSyncService Sync)> SetupAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "SugarERP-KitchenCost-tests", Guid.NewGuid().ToString("N"), "kitchen.db");
        var store = new KitchenStore(path); await store.InitializeAsync();
        await using var db = store.Open();
        db.Configuration.Add(new KitchenConfiguration { ApiBaseUrl = "https://sync.test/api/v1", SiteId = Guid.NewGuid(),
            DeviceId = Guid.NewGuid(), ProtectedCredential = DeviceCredentialProtector.Protect("device-secret"), StreamEpoch = 1 });
        await db.SaveChangesAsync();
        return (store, new KitchenSyncService(new HttpClient(), store));
    }
}
