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
        await using (var separated = store.Open())
        {
            Assert.False(await separated.Products.AnyAsync(x => x.Id == flour.Id || x.Id == eggs.Id));
            Assert.Equal(2, await separated.Ingredients.CountAsync(x => x.ItemId == flour.Id || x.ItemId == eggs.Id));
        }
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
    public async Task CodexTestCakeThreeUnitsDeductNineEggsAndSixHundredGramsFlour()
    {
        var (store, sync) = await SetupAsync();
        var eggs = await sync.SaveCatalogItemAsync(null, null, "CODEX_TEST_EGGS", "قطعة", 1, "INGREDIENT");
        var flour = await sync.SaveCatalogItemAsync(null, null, "CODEX_TEST_FLOUR", "g", 1, "INGREDIENT");
        var cake = await sync.SaveCatalogItemAsync(null, null, "CODEX_TEST_CAKE", "قطعة", 1, "PRODUCT");
        await sync.ReceiveIngredientPurchaseAsync(eggs.Id, 30, 3_000, "CODEX test stock");
        await sync.ReceiveIngredientPurchaseAsync(flour.Id, 2_000, 20_000, "CODEX test stock");
        await sync.SaveRecipeAsync(cake.Id, 0, 1, new Dictionary<Guid, long> { [eggs.Id] = 3, [flour.Id] = 200 });
        var request = await AddRequestAsync(store, cake, 3);

        await sync.DispatchAsync(request.RequestId, new Dictionary<Guid, long> { [request.LineId] = 3 });

        await using var verify = store.Open();
        Assert.Equal(21, (await verify.Ingredients.SingleAsync(x => x.ItemId == eggs.Id)).QuantityScaled);
        Assert.Equal(1_400, (await verify.Ingredients.SingleAsync(x => x.ItemId == flour.Id)).QuantityScaled);
        var movements = await verify.IngredientMovements.Where(x => x.Kind == "DISPATCH").ToDictionaryAsync(x => x.IngredientItemId);
        Assert.Equal(-9, movements[eggs.Id].DeltaScaled);
        Assert.Equal(-600, movements[flour.Id].DeltaScaled);
        Assert.Equal(2, movements.Count);
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

    [Fact]
    public async Task UnrelatedProductWithoutRecipeDoesNotBlockDispatch_ButIncludedProductFailsAtomically()
    {
        var (store, sync) = await SetupAsync();
        var eggs = await sync.SaveCatalogItemAsync(null, null, "Eggs", "قطعة", 1, "INGREDIENT");
        var cakeWithRecipe = await sync.SaveCatalogItemAsync(null, null, "Cake A", "قطعة", 1, "PRODUCT");
        var cakeWithoutRecipe = await sync.SaveCatalogItemAsync(null, null, "Cake C", "قطعة", 1, "PRODUCT");
        await sync.ReceiveIngredientPurchaseAsync(eggs.Id, 30, 3_000, "test stock");
        await sync.SaveRecipeAsync(cakeWithRecipe.Id, 0, 1, new Dictionary<Guid, long> { [eggs.Id] = 3 });

        var validRequest = await AddRequestAsync(store, cakeWithRecipe, 3);
        await sync.DispatchAsync(validRequest.RequestId, new Dictionary<Guid, long> { [validRequest.LineId] = 3 });
        await using (var verifyValid = store.Open())
            Assert.Equal(21, (await verifyValid.Ingredients.SingleAsync(x => x.ItemId == eggs.Id)).QuantityScaled);

        var invalidRequest = await AddRequestAsync(store, cakeWithoutRecipe, 1);
        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() => sync.DispatchAsync(invalidRequest.RequestId,
            new Dictionary<Guid, long> { [invalidRequest.LineId] = 1 }));
        Assert.Equal("RECIPE_REQUIRED", exception.Code);
        Assert.Contains("Cake C", exception.Message);
        await using var verifyFailure = store.Open();
        Assert.Equal(21, (await verifyFailure.Ingredients.SingleAsync(x => x.ItemId == eggs.Id)).QuantityScaled);
        Assert.Equal(1, await verifyFailure.IngredientMovements.CountAsync(x => x.Kind == "DISPATCH"));
        Assert.Equal("RECEIVED", (await verifyFailure.Requests.SingleAsync(x => x.Id == invalidRequest.RequestId)).Status);
    }

    [Fact]
    public async Task ProductRenameAndRestartKeepRecipeIdMapping()
    {
        var (store, sync) = await SetupAsync();
        var eggs = await sync.SaveCatalogItemAsync(null, null, "Eggs", "قطعة", 1, "INGREDIENT");
        var cake = await sync.SaveCatalogItemAsync(null, null, "Old Cake Name", "قطعة", 1, "PRODUCT");
        await sync.SaveRecipeAsync(cake.Id, 0, 1, new Dictionary<Guid, long> { [eggs.Id] = 3 });
        var renamed = await sync.SaveCatalogItemAsync(cake.Id, cake.Version, "Renamed Cake", "قطعة", 1, "PRODUCT");

        var restarted = new KitchenStore(GetDatabasePath(store));
        await restarted.InitializeAsync();
        await using var verify = restarted.Open();
        Assert.Equal(cake.Id, (await verify.Recipes.SingleAsync()).ProductItemId);
        Assert.Equal(eggs.Id, (await verify.RecipeComponents.SingleAsync()).IngredientItemId);
        Assert.Equal("Renamed Cake", (await verify.Products.SingleAsync(x => x.Id == cake.Id)).Name);
        Assert.Equal(renamed.Id, cake.Id);
    }

    [Fact]
    public async Task PurchaseAdvancesStockVersionWithoutCorruptingCatalogVersion()
    {
        var (store, sync) = await SetupAsync();
        var flour = await sync.SaveCatalogItemAsync(null, null, "Flour", "g", 1, "INGREDIENT");

        await sync.ReceiveIngredientPurchaseAsync(flour.Id, 1_000, 50_000, "First purchase");

        await using (var verifyPurchase = store.Open())
        {
            var balance = await verifyPurchase.Ingredients.SingleAsync(x => x.ItemId == flour.Id);
            Assert.Equal(1, balance.Version);
            Assert.Equal(1, balance.StockVersion);
        }
        var renamed = await sync.SaveCatalogItemAsync(flour.Id, flour.Version, "Premium Flour", "g", 1, "INGREDIENT");
        Assert.Equal(2, renamed.Version);
        await using var verifyRename = store.Open();
        var saved = await verifyRename.Ingredients.SingleAsync(x => x.ItemId == flour.Id);
        Assert.Equal("Premium Flour", saved.Name);
        Assert.Equal(2, saved.Version);
        Assert.Equal(1, saved.StockVersion);
    }

    [Fact]
    public async Task NewIngredientAndOpeningPurchaseAreAtomicSeparatedAndPersistent()
    {
        var (store, sync) = await SetupAsync();

        var flour = await sync.CreateIngredientWithOpeningPurchaseAsync("Flour", "kg", 1_000, 2_500, 75_000);

        await using (var verify = store.Open())
        {
            Assert.False(await verify.Products.AnyAsync(x => x.Id == flour.ItemId));
            var saved = await verify.Ingredients.SingleAsync(x => x.ItemId == flour.ItemId);
            Assert.Equal("kg", saved.Unit);
            Assert.Equal(1_000, saved.QuantityScale);
            Assert.Equal(2_500, saved.QuantityScaled);
            Assert.Equal(75_000, saved.InventoryCostMinor);
            Assert.Equal(1, saved.Version);
            Assert.Equal(1, saved.StockVersion);
            Assert.Equal(2, await verify.Outbox.CountAsync());
            Assert.Equal(new[] { 1, 2 }, await verify.Outbox.OrderBy(x => x.Sequence).Select(x => x.Sequence).ToArrayAsync());
            Assert.Contains("catalog.item.updated", (await verify.Outbox.OrderBy(x => x.Sequence).FirstAsync()).UploadJson);
            Assert.Contains("ingredient.received", (await verify.Outbox.OrderBy(x => x.Sequence).LastAsync()).UploadJson);
            Assert.Equal(2_500, (await verify.IngredientMovements.SingleAsync()).DeltaScaled);
        }

        var restarted = new KitchenStore(GetDatabasePath(store));
        await restarted.InitializeAsync();
        await using var afterRestart = restarted.Open();
        Assert.Equal(flour.ItemId, (await afterRestart.Ingredients.SingleAsync()).ItemId);
        Assert.Equal(2_500, (await afterRestart.Ingredients.SingleAsync()).QuantityScaled);
    }

    [Fact]
    public async Task FuturePurchasesUseSavedIngredientCostWithoutChangingHistoricalReceipts()
    {
        var (store, sync) = await SetupAsync();
        var eggs = await sync.CreateIngredientWithOpeningPurchaseAsync("Eggs", "قطعة", 1, 30, 10_000);
        await sync.UpdateIngredientPurchaseUnitCostAsync(eggs.ItemId, 3_333_000);
        var total = await sync.ReceiveIngredientPurchaseAtSavedCostAsync(eggs.ItemId, 100, "purchase");
        Assert.Equal(33_330, total);
        await using var db = store.Open();
        var movements = (await db.IngredientMovements.ToArrayAsync()).OrderBy(x => x.OccurredAtUtc).ToArray();
        Assert.Equal(10_000, movements[0].CostMinor);
        Assert.Equal(33_330, movements[1].CostMinor);
        Assert.Equal(130, (await db.Ingredients.SingleAsync()).QuantityScaled);
    }

    [Fact]
    public async Task RepeatedRecipeEditsKeepOneRecipeAndOneRowPerIngredient()
    {
        var (store, sync) = await SetupAsync();
        var eggs = await sync.CreateIngredientWithOpeningPurchaseAsync("Eggs", "قطعة", 1, 30, 10_000);
        var cake = await sync.SaveCatalogItemAsync(null, null, "Cake", "قطعة", 1, "PRODUCT", 18_000);
        await sync.SaveRecipeAsync(cake.Id, 0, 1, new Dictionary<Guid, long> { [eggs.ItemId] = 2 });
        await sync.SaveRecipeAsync(cake.Id, 1, 1, new Dictionary<Guid, long> { [eggs.ItemId] = 4 });
        await sync.SaveRecipeAsync(cake.Id, 2, 1, new Dictionary<Guid, long> { [eggs.ItemId] = 3 });
        await using var db = store.Open();
        Assert.Equal(1, await db.Recipes.CountAsync(x => x.ProductItemId == cake.Id));
        var component = await db.RecipeComponents.SingleAsync(x => x.ProductItemId == cake.Id);
        Assert.Equal(3, component.QuantityScaled);
        Assert.Equal(3, (await db.Recipes.SingleAsync(x => x.ProductItemId == cake.Id)).Version);
    }

    [Fact]
    public async Task ArchiveIngredientHidesItButKeepsHistoricalMovement()
    {
        var (store, sync) = await SetupAsync();
        var cream = await sync.CreateIngredientWithOpeningPurchaseAsync("Cream", "kg", 1_000, 10_000, 350_000);
        await sync.ArchiveCatalogItemAsync(cream.ItemId);
        await using var db = store.Open();
        Assert.False((await db.Ingredients.SingleAsync(x => x.ItemId == cream.ItemId)).Active);
        Assert.Equal(1, await db.IngredientMovements.CountAsync(x => x.IngredientItemId == cream.ItemId));
        Assert.Contains("catalog.item.archived", (await db.Outbox.OrderByDescending(x => x.Sequence).FirstAsync()).UploadJson);
    }

    [Fact]
    public async Task WasteDeductsOncePersistsReasonAndCostAndRejectsNegativeStock()
    {
        var (store, sync) = await SetupAsync();
        var eggs = await sync.CreateIngredientWithOpeningPurchaseAsync("Eggs", "قطعة", 1, 100, 33_330);

        await sync.RecordWasteAsync(new Dictionary<Guid, long> { [eggs.ItemId] = 12 }, "انتهت الصلاحية");
        await Assert.ThrowsAsync<InvalidOperationException>(() => sync.RecordWasteAsync(
            new Dictionary<Guid, long> { [eggs.ItemId] = 89 }, "أكثر من المتاح"));

        var restarted = new KitchenStore(GetDatabasePath(store));
        await restarted.InitializeAsync();
        await using var verify = restarted.Open();
        var balance = await verify.Ingredients.SingleAsync(x => x.ItemId == eggs.ItemId);
        Assert.Equal(88, balance.QuantityScaled);
        Assert.True(balance.Active);
        var waste = await verify.IngredientMovements.SingleAsync(x => x.Kind == "WASTE");
        Assert.Equal(-12, waste.DeltaScaled);
        Assert.Equal(4_000, waste.CostMinor);
        Assert.Equal("انتهت الصلاحية", waste.Reason);
        Assert.Single(await verify.Outbox.Where(x => x.UploadJson.Contains("ingredient.waste")).ToListAsync());
    }

    private static async Task<(Guid RequestId, Guid LineId)> AddRequestAsync(KitchenStore store, KitchenProduct product, long quantity)
    {
        var requestId = Guid.NewGuid(); var lineId = Guid.NewGuid();
        await using var db = store.Open();
        db.Requests.Add(new KitchenRequestRecord { Id = requestId, BranchSiteId = Guid.NewGuid(), BranchName = "Test Branch",
            Status = "RECEIVED", Version = 1, SubmittedAtUtc = DateTimeOffset.UtcNow,
            Lines = [new KitchenRequestLineRecord { Id = lineId, RequestId = requestId, ItemId = product.Id,
                Name = product.Name, Unit = product.Unit, QuantityScale = product.QuantityScale, RequestedScaled = quantity }] });
        await db.SaveChangesAsync();
        return (requestId, lineId);
    }

    private static string GetDatabasePath(KitchenStore store)
    {
        using var db = store.Open();
        return db.Database.GetDbConnection().DataSource;
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
