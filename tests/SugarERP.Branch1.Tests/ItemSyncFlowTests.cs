using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SugarERP.Application;
using SugarERP.Domain;
using SugarERP.Infrastructure.Local;
using SugarERP.Kitchen;
using SugarERP.Sync.Client;
using Xunit;

namespace SugarERP.Branch1.Tests;

public sealed class ItemSyncFlowTests
{
    [Fact]
    public async Task CrossSiteCatalogUpdatePreservesBranchSpecificPriceAndIsIdempotent()
    {
        var setup = await CreateBranchAsync();
        var database = setup.Database;
        var branchSiteId = setup.SiteId;
        var branchDeviceId = setup.DeviceId;
        var itemId = setup.ItemId;
        var sourceSiteId = Guid.NewGuid();
        var sourceDeviceId = Guid.NewGuid();
        var handler = new BranchCatalogHandler(branchSiteId, branchDeviceId, sourceSiteId, sourceDeviceId, itemId);
        var sync = new BranchSyncService(new HttpClient(handler), database);

        var first = await sync.SynchronizeAsync();
        var replay = await sync.SynchronizeAsync();

        Assert.True(first.Succeeded, first.UserMessage);
        Assert.True(replay.Succeeded, replay.UserMessage);
        await using var db = database.CreateContext();
        var item = await db.CatalogItems.SingleAsync();
        Assert.Equal("Chocolate Cake Updated", item.NameAr);
        Assert.Equal(12_500, item.RetailPriceMinor);
        Assert.Equal(2, item.Version);
        Assert.Equal(1, await db.CatalogItems.CountAsync());
    }

    [Fact]
    public async Task BranchType1BootstrapDownloadsCafeCustomersAndPrices()
    {
        var setup = await CreateBranchAsync(DeviceProfile.BranchType1);
        var customerId = Guid.NewGuid();
        var handler = new BranchCafeBootstrapHandler(setup.SiteId, setup.ItemId, customerId);

        var result = await new BranchSyncService(new HttpClient(handler), setup.Database).SynchronizeAsync();

        Assert.True(result.Succeeded, result.UserMessage);
        await using var db = setup.Database.CreateContext();
        var customer = await db.CafeCustomers.Include(value => value.Prices).SingleAsync();
        Assert.Equal(customerId, customer.Id);
        Assert.Equal("Bouna Seira", customer.Name);
        Assert.Equal(12_500, Assert.Single(customer.Prices).UnitPriceMinor);
    }

    [Fact]
    public async Task BranchType1BootstrapPreservesLegacyCustomerHistoryWhenServerPhoneConflicts()
    {
        var setup = await CreateBranchAsync(DeviceProfile.BranchType1);
        var legacyId = Guid.NewGuid();
        await using (var seed = setup.Database.CreateContext())
        {
            seed.CafeCustomers.Add(new CafeCustomer
            {
                Id = legacyId,
                CommandId = Guid.NewGuid(),
                Name = "Legacy local profile",
                Kind = "كافيه",
                Phone = "01000000000",
                Address = "",
                Active = true,
                Version = 1,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        var serverCustomerId = Guid.NewGuid();
        var result = await new BranchSyncService(
            new HttpClient(new BranchCafeBootstrapHandler(setup.SiteId, setup.ItemId, serverCustomerId)),
            setup.Database).SynchronizeAsync();

        Assert.True(result.Succeeded, result.UserMessage);
        await using var db = setup.Database.CreateContext();
        var serverCustomer = await db.CafeCustomers.SingleAsync(value => value.Id == serverCustomerId);
        var legacyCustomer = await db.CafeCustomers.SingleAsync(value => value.Id == legacyId);
        Assert.True(serverCustomer.Active);
        Assert.Equal("01000000000", serverCustomer.Phone);
        Assert.False(legacyCustomer.Active);
        Assert.Equal($"legacy-{legacyId:N}", legacyCustomer.Phone);
    }

    [Fact]
    public async Task BranchBootstrapRetiresLegacyIngredientAndNeverAddsItAsSellableProduct()
    {
        var setup = await CreateBranchAsync(DeviceProfile.BranchType1);
        var ingredientId = Guid.NewGuid();
        await using (var seed = setup.Database.CreateContext())
        {
            seed.CatalogItems.Add(new CatalogItem { Id = ingredientId, Sku = "EGG-LEGACY", NameAr = "Eggs", Unit = "قطعة",
                QuantityScale = 1, RetailPriceMinor = 0, Active = true, Version = 1, UpdatedAtUtc = DateTimeOffset.UtcNow,
                StockBalance = new StockBalance { ItemId = ingredientId, Revision = 1, AsOfUtc = DateTimeOffset.UtcNow } });
            await seed.SaveChangesAsync();
        }

        var result = await new BranchSyncService(new HttpClient(
            new BranchIngredientBootstrapHandler(setup.SiteId, setup.ItemId, ingredientId)), setup.Database).SynchronizeAsync();

        Assert.True(result.Succeeded, result.UserMessage);
        await using var verify = setup.Database.CreateContext();
        Assert.True((await verify.CatalogItems.SingleAsync(x => x.Id == setup.ItemId)).Active);
        Assert.False((await verify.CatalogItems.SingleAsync(x => x.Id == ingredientId)).Active);
        Assert.Single(await verify.CatalogItems.Where(x => x.Active).ToListAsync());
    }

    [Fact]
    public async Task KitchenCreatedItemSurvivesOfflineRetryAndUploadsSameEventOnce()
    {
        var path = Path.Combine(Path.GetTempPath(), "SugarERP-ItemSync-tests", Guid.NewGuid().ToString(), "kitchen.db");
        var store = new KitchenStore(path); await store.InitializeAsync();
        await using (var db = store.Open())
        {
            db.Configuration.Add(new KitchenConfiguration
            {
                ApiBaseUrl = "https://sync.test/api/v1", SiteId = Guid.NewGuid(), DeviceId = Guid.NewGuid(),
                ProtectedCredential = DeviceCredentialProtector.Protect("device-secret"), StreamEpoch = 1
            });
            await db.SaveChangesAsync();
        }

        var offline = new KitchenSyncService(new HttpClient(new OfflineHandler()), store);
        var created = await offline.SaveCatalogItemAsync(null, null, "New Kitchen Cake", "قطعة", 1);
        await offline.FlushAsync(forceRetry: true);
        await using (var pending = store.Open())
        {
            var row = await pending.Outbox.SingleAsync();
            Assert.False(row.Acknowledged);
            Assert.Equal("OFFLINE", row.LastErrorCode);
            Assert.Equal(created.Id, row.RequestId);
        }

        var onlineHandler = new CatalogPushHandler();
        var restartedStore = new KitchenStore(path); await restartedStore.InitializeAsync();
        await new KitchenSyncService(new HttpClient(onlineHandler), restartedStore).FlushAsync(forceRetry: true);

        await using var verify = restartedStore.Open();
        Assert.Single(await verify.Products.ToListAsync());
        Assert.True((await verify.Outbox.SingleAsync()).Acknowledged);
        Assert.Equal(1, onlineHandler.PushCount);
        Assert.Equal(created.Id, onlineHandler.ItemId);
    }

    private static async Task<(LocalDatabase Database, Guid SiteId, Guid DeviceId, Guid ItemId)> CreateBranchAsync(DeviceProfile profile = DeviceProfile.BranchType2)
    {
        var siteId = Guid.NewGuid(); var deviceId = Guid.NewGuid(); var itemId = Guid.NewGuid();
        var path = Path.Combine(Path.GetTempPath(), "SugarERP-ItemSync-tests", Guid.NewGuid().ToString(), "branch.db");
        var database = new LocalDatabase(path); await database.InitializeAsync();
        await using var db = database.CreateContext();
        db.DeviceConfigurations.Add(new DeviceConfiguration
        {
            SiteId = siteId, DeviceId = deviceId, Profile = profile, SiteName = profile == DeviceProfile.BranchType1 ? "Branch 1" : "Branch 2",
            ApiBaseUrl = "https://sync.test/api/v1", DeviceCredential = DeviceCredentialProtector.Protect("device-secret"),
            StreamEpoch = 1, EnrolledAtUtc = DateTimeOffset.UtcNow
        });
        db.CatalogItems.Add(new CatalogItem
        {
            Id = itemId, Sku = "CAKE-1", NameAr = "Chocolate Cake", Unit = "قطعة", QuantityScale = 1,
            RetailPriceMinor = 12_500, Active = true, Version = 1, UpdatedAtUtc = DateTimeOffset.UtcNow,
            StockBalance = new StockBalance { ItemId = itemId, Revision = 1, AsOfUtc = DateTimeOffset.UtcNow }
        });
        await db.SaveChangesAsync();
        return (database, siteId, deviceId, itemId);
    }

    private sealed class BranchCafeBootstrapHandler(Guid siteId, Guid itemId, Guid customerId) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/sync/bootstrap", StringComparison.Ordinal))
                return Task.FromResult(Ok(new
                {
                    contract_version = "1.0",
                    site = new { id = siteId, name = "Branch 1", type = "BRANCH_TYPE_1", timezone = "Africa/Cairo" },
                    catalog = new[] { new { id = itemId, sku = "CAKE-1", nameAr = "Chocolate Cake", unit = "قطعة", quantityScale = 1, retailPriceMinor = 12_500, active = true, version = 1 } },
                    customers = new[] { new { id = customerId, name = "Bouna Seira", contact = "01000000000", notes = "Cafe", active = true, version = 1,
                        prices = new[] { new { itemId, priceMinor = 12_500, version = 1 } } } },
                    recipes = Array.Empty<object>(), stock = Array.Empty<object>(), as_of = DateTimeOffset.UtcNow
                }));
            if (path.EndsWith("/sync/pull", StringComparison.Ordinal))
                return Task.FromResult(Ok(new { contract_version = "1.0", compatibility = new { minimum = "1.0", current = "1.0" }, cursor = "cafe-cursor", has_more = false, events = Array.Empty<object>() }));
            if (path.EndsWith("/sync/ack", StringComparison.Ordinal))
                return Task.FromResult(Ok(new { acknowledged = true, server_position = "0" }));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class BranchIngredientBootstrapHandler(Guid siteId, Guid productId, Guid ingredientId) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/sync/bootstrap", StringComparison.Ordinal))
                return Task.FromResult(Ok(new
                {
                    contract_version = "1.0",
                    site = new { id = siteId, name = "Branch 1", type = "BRANCH_TYPE_1", timezone = "Africa/Cairo" },
                    catalog = new object[]
                    {
                        new { id = productId, sku = "CAKE-1", nameAr = "Chocolate Cake", unit = "قطعة", quantityScale = 1,
                            retailPriceMinor = 12_500, kind = "PRODUCT", active = true, version = 1 },
                        new { id = ingredientId, sku = "EGG-LEGACY", nameAr = "Eggs", unit = "قطعة", quantityScale = 1,
                            retailPriceMinor = 0, kind = "INGREDIENT", active = true, version = 1 }
                    },
                    customers = Array.Empty<object>(), recipes = Array.Empty<object>(), stock = Array.Empty<object>(), as_of = DateTimeOffset.UtcNow
                }));
            if (path.EndsWith("/sync/pull", StringComparison.Ordinal))
                return Task.FromResult(Ok(new { contract_version = "1.0", compatibility = new { minimum = "1.0", current = "1.0" }, cursor = "ingredient-filter-cursor", has_more = false, events = Array.Empty<object>() }));
            if (path.EndsWith("/sync/ack", StringComparison.Ordinal))
                return Task.FromResult(Ok(new { acknowledged = true, server_position = "0" }));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class BranchCatalogHandler : HttpMessageHandler
    {
        private readonly object _bootstrap;
        private readonly object _page;
        private int _pulls;

        public BranchCatalogHandler(Guid targetSiteId, Guid targetDeviceId, Guid sourceSiteId, Guid sourceDeviceId, Guid itemId)
        {
            _bootstrap = new { contract_version = "1.0", site = new { id = targetSiteId, name = "Branch 2", type = "BRANCH_TYPE_2", timezone = "Africa/Cairo" },
                catalog = new[] { new { id = itemId, sku = "CAKE-1", nameAr = "Chocolate Cake", unit = "قطعة", quantityScale = 1,
                    retailPriceMinor = 12_500, active = true, version = 1 } }, customers = Array.Empty<object>(), recipes = Array.Empty<object>(), stock = Array.Empty<object>(), as_of = DateTimeOffset.UtcNow };
            const string occurredAt = "2026-09-22T20:00:00.1234567+00:00";
            var payload = JsonSerializer.SerializeToElement(new { item_id = itemId, site_id = sourceSiteId, price_site_id = sourceSiteId,
                sku = "CAKE-1", name_ar = "Chocolate Cake Updated", unit = "قطعة", quantity_scale = 1,
                retail_price_minor = 10_000, kind = "PRODUCT", active = true, version = 2 });
            var eventId = Guid.NewGuid();
            var hash = ContractEventFactory.ComputeHash(eventId, 1, "catalog.item.updated", 1, occurredAt, payload, []);
            _page = new { contract_version = "1.0", compatibility = new { minimum = "1.0", current = "1.0" }, cursor = "catalog-cursor", has_more = false,
                events = new[] { new { id = eventId, origin_device_id = sourceDeviceId, origin_site_id = sourceSiteId, stream_epoch = 1,
                    device_sequence = 1, event_type = "catalog.item.updated", schema_version = 1, occurred_at = occurredAt,
                    received_at = occurredAt, payload, dependencies = Array.Empty<Guid>(), content_hash = hash, server_position = "10" } } };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/sync/bootstrap", StringComparison.Ordinal)) return Task.FromResult(Ok(_bootstrap));
            if (path.EndsWith("/sync/pull", StringComparison.Ordinal))
                return Task.FromResult(Ok(Interlocked.Increment(ref _pulls) == 1 ? _page : new { contract_version = "1.0", compatibility = new { minimum = "1.0", current = "1.0" }, cursor = "catalog-cursor", has_more = false, events = Array.Empty<object>() }));
            if (path.EndsWith("/sync/ack", StringComparison.Ordinal)) return Task.FromResult(Ok(new { acknowledged = true, server_position = "10" }));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }

    private sealed class CatalogPushHandler : HttpMessageHandler
    {
        public int PushCount { get; private set; }
        public Guid ItemId { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var row = body.RootElement.GetProperty("events")[0];
            PushCount++;
            ItemId = row.GetProperty("payload").GetProperty("item_id").GetGuid();
            var eventId = row.GetProperty("id").GetGuid();
            return Ok(new { contract_version = "1.0", next_expected_sequence = 2,
                results = new[] { new { id = eventId, status = "accepted", server_position = "1", code = (string?)null, error_code = (string?)null, retryable = (bool?)null } } });
        }
    }

    private static HttpResponseMessage Ok<T>(T body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
}
