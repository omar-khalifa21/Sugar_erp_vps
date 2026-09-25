using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SugarERP.Application;
using SugarERP.Infrastructure.Local;
using SugarERP.Kitchen;
using Xunit;

namespace SugarERP.Branch1.Tests;

public sealed class KitchenStoreUpgradeTests
{
    [Fact]
    public async Task PermanentDelete_RemovesOnlyNeverSyncedUnusedItemAndRepairsSequence()
    {
        var path = Path.Combine(Path.GetTempPath(), "SugarERP-Kitchen-tests", Guid.NewGuid().ToString(), "kitchen.db");
        var store = new KitchenStore(path); await store.InitializeAsync();
        await using (var db = store.Open())
        {
            db.Configuration.Add(new KitchenConfiguration { SiteId = Guid.NewGuid(), DeviceId = Guid.NewGuid(), NextDeviceSequence = 1 });
            await db.SaveChangesAsync();
        }
        var service = new KitchenSyncService(new HttpClient(new OfflineHandler()), store);
        var created = await service.SaveCatalogItemAsync(null, null, "Unsynced Cake", "قطعة", 1, "PRODUCT", 10_000);

        await service.PermanentlyDeleteUnsyncedCatalogItemAsync(created.Id);

        await using var verify = store.Open();
        Assert.False(await verify.Products.AnyAsync(x => x.Id == created.Id));
        Assert.False(await verify.Outbox.AnyAsync(x => x.RequestId == created.Id));
        Assert.Equal(1, (await verify.Configuration.SingleAsync()).NextDeviceSequence);
    }

    [Fact]
    public async Task PermanentDelete_BlocksHistoricallyReferencedItemWithoutChangingData()
    {
        var path = Path.Combine(Path.GetTempPath(), "SugarERP-Kitchen-tests", Guid.NewGuid().ToString(), "kitchen.db");
        var store = new KitchenStore(path); await store.InitializeAsync();
        var productId = Guid.NewGuid();
        await using (var db = store.Open())
        {
            db.Configuration.Add(new KitchenConfiguration { SiteId = Guid.NewGuid(), DeviceId = Guid.NewGuid(), NextDeviceSequence = 2 });
            db.Products.Add(new KitchenProduct { Id = productId, Name = "Historical Cake", Unit = "قطعة", QuantityScale = 1, Version = 1 });
            db.Recipes.Add(new KitchenRecipeRecord { ProductItemId = productId, OutputScaled = 1, Version = 1 });
            await db.SaveChangesAsync();
        }

        var service = new KitchenSyncService(new HttpClient(new OfflineHandler()), store);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PermanentlyDeleteUnsyncedCatalogItemAsync(productId));

        Assert.Contains("مستخدم", error.Message);
        await using var verify = store.Open();
        Assert.True(await verify.Products.AnyAsync(x => x.Id == productId));
        Assert.True(await verify.Recipes.AnyAsync(x => x.ProductItemId == productId));
    }

    [Fact]
    public async Task AdditiveUpgradePreservesEnrollmentAndReceiptHistoryAcrossRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), "SugarERP-Kitchen-tests", Guid.NewGuid().ToString(), "kitchen.db");
        var store = new KitchenStore(path);
        await store.InitializeAsync();
        var deviceId = Guid.NewGuid();
        await using (var db = store.Open())
        {
            db.Configuration.Add(new KitchenConfiguration { DeviceId = deviceId, SiteId = Guid.NewGuid(), ProtectedCredential = "test protected value", NextDeviceSequence = 12 });
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("DROP TABLE shipment_receipts; DROP TABLE outbox;");
        }
        await store.InitializeAsync();
        var receiptId = Guid.NewGuid();
        await using (var db = store.Open())
        {
            Assert.Equal(deviceId, (await db.Configuration.SingleAsync()).DeviceId);
            Assert.Equal(12, (await db.Configuration.SingleAsync()).NextDeviceSequence);
            db.Receipts.Add(new KitchenReceiptRecord { EventId = Guid.NewGuid(), ReceiptId = receiptId, ShipmentId = Guid.NewGuid(), BranchSiteId = Guid.NewGuid(), Status = "DISPUTED", PayloadJson = "{\"counted_scaled\":0}", CountedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        var restarted = new KitchenStore(path);
        await restarted.InitializeAsync();
        await using var read = restarted.Open();
        Assert.Equal(receiptId, (await read.Receipts.SingleAsync()).ReceiptId);
        Assert.Equal(deviceId, (await read.Configuration.SingleAsync()).DeviceId);
    }

    [Fact]
    public async Task ArchiveCafeCustomer_QueuesDurableEventAndKeepsHistoricalOrders()
    {
        var path = Path.Combine(Path.GetTempPath(), "SugarERP-Kitchen-tests", Guid.NewGuid().ToString(), "kitchen.db");
        var store = new KitchenStore(path);
        await store.InitializeAsync();
        var customerId = Guid.NewGuid();
        await using (var db = store.Open())
        {
            db.Configuration.Add(new KitchenConfiguration { SiteId = Guid.NewGuid(), DeviceId = Guid.NewGuid(), NextDeviceSequence = 8 });
            db.CafeCustomers.Add(new KitchenCafeCustomer { Id = customerId, Name = "كافيه قديم", Version = 3 });
            db.CustomOrders.Add(new KitchenCustomOrder { Id = Guid.NewGuid(), CustomerId = customerId, CustomerName = "كافيه قديم", OrderNumber = "K-CF-HISTORY", CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow, DueAtUtc = DateTimeOffset.UtcNow.AddDays(1) });
            await db.SaveChangesAsync();
        }

        var service = new KitchenSyncService(new HttpClient(new OfflineHandler()), store);
        await service.ArchiveCafeCustomerAsync(customerId);
        await new KitchenStore(path).InitializeAsync();

        await using var verify = store.Open();
        var customer = await verify.CafeCustomers.SingleAsync(x => x.Id == customerId);
        Assert.True(customer.HiddenLocally);
        Assert.False(customer.Active);
        Assert.Equal(4, customer.Version);
        var queued = await verify.Outbox.SingleAsync();
        Assert.Equal(8, queued.Sequence);
        Assert.True(queued.AppliedLocally);
        Assert.Contains("cafe_customer.archived", queued.UploadJson);
        Assert.Equal(9, (await verify.Configuration.SingleAsync()).NextDeviceSequence);
        Assert.Equal("K-CF-HISTORY", (await service.GetCustomOrdersAsync()).Single().OrderNumber);
    }

    [Fact]
    public async Task ResetConnection_PreservesBusinessDataAndRemovesOnlyDeviceIdentity()
    {
        var path = Path.Combine(Path.GetTempPath(), "SugarERP-Kitchen-tests", Guid.NewGuid().ToString(), "kitchen.db");
        var store = new KitchenStore(path); await store.InitializeAsync();
        var productId = Guid.NewGuid();
        await using (var db = store.Open())
        {
            db.Configuration.Add(new KitchenConfiguration { ApiBaseUrl = "https://ascendyz.xyz/api/v1", SiteId = Guid.NewGuid(),
                DeviceId = Guid.NewGuid(), ProtectedCredential = DeviceCredentialProtector.Protect("old-secret"), StreamEpoch = 3,
                NextDeviceSequence = 17, Cursor = "old-cursor", PrinterName = "Kitchen Printer" });
            db.Products.Add(new KitchenProduct { Id = productId, Name = "Historical Cake", Active = true });
            db.Outbox.Add(new KitchenOutbox { EventId = Guid.NewGuid(), RequestId = Guid.NewGuid(), Sequence = 1,
                UploadJson = "{}", Acknowledged = true, AppliedLocally = true, NextAttemptAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        await new KitchenSyncService(new HttpClient(new OfflineHandler()), store).ResetConnectionAsync();

        await using var verify = store.Open();
        var configuration = await verify.Configuration.SingleAsync();
        Assert.Equal(Guid.Empty, configuration.DeviceId);
        Assert.Equal(Guid.Empty, configuration.SiteId);
        Assert.Equal(string.Empty, configuration.ProtectedCredential);
        Assert.Null(configuration.Cursor);
        Assert.Equal(1, configuration.NextDeviceSequence);
        Assert.Equal("Kitchen Printer", configuration.PrinterName);
        Assert.Equal(productId, (await verify.Products.SingleAsync()).Id);
        verify.ChangeTracker.Clear();
        var newSiteId = Guid.NewGuid(); var newDeviceId = Guid.NewGuid();
        var reconnectedService = new KitchenSyncService(new HttpClient(new KitchenEnrollmentHandler(newSiteId, newDeviceId)), store);
        await reconnectedService.EnrollAsync(new Uri("https://ascendyz.xyz/api/v1"), "new-one-time-key", "Kitchen PC");
        await reconnectedService.SaveCafeAsync(null, null, "Reconnect Cafe", "");
        Assert.Equal(2, await verify.Outbox.CountAsync());
        Assert.Equal(2, await verify.Outbox.CountAsync(x => x.Sequence == 1));
        verify.ChangeTracker.Clear();
        var enrolled = await verify.Configuration.SingleAsync();
        Assert.Equal(newSiteId, enrolled.SiteId);
        Assert.Equal(newDeviceId, enrolled.DeviceId);
        Assert.Equal("Kitchen Printer", enrolled.PrinterName);
    }

    [Fact]
    public async Task ResetConnection_IsBlockedWhileUnsyncedOperationsExist()
    {
        var path = Path.Combine(Path.GetTempPath(), "SugarERP-Kitchen-tests", Guid.NewGuid().ToString(), "kitchen.db");
        var store = new KitchenStore(path); await store.InitializeAsync();
        var deviceId = Guid.NewGuid();
        await using (var db = store.Open())
        {
            db.Configuration.Add(new KitchenConfiguration { SiteId = Guid.NewGuid(), DeviceId = deviceId, ProtectedCredential = "protected" });
            db.Outbox.Add(new KitchenOutbox { EventId = Guid.NewGuid(), RequestId = Guid.NewGuid(), Sequence = 1,
                UploadJson = "{}", AppliedLocally = true, NextAttemptAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new KitchenSyncService(new HttpClient(new OfflineHandler()), store).ResetConnectionAsync());

        Assert.Equal("PENDING_SYNC_EXISTS", error.Code);
        await using var verify = store.Open();
        Assert.Equal(deviceId, (await verify.Configuration.SingleAsync()).DeviceId);
        Assert.Single(await verify.Outbox.ToListAsync());
    }

    [Fact]
    public async Task CustomOrders_LoadNewestFirst_WithSqliteDateTimeOffsets()
    {
        var path = Path.Combine(Path.GetTempPath(), "SugarERP-Kitchen-tests", Guid.NewGuid().ToString(), "kitchen.db");
        var store = new KitchenStore(path);
        await store.InitializeAsync();
        var older = DateTimeOffset.UtcNow.AddHours(-1);
        var newer = DateTimeOffset.UtcNow;
        await using (var db = store.Open())
        {
            db.CustomOrders.AddRange(
                new KitchenCustomOrder { Id = Guid.NewGuid(), CustomerId = Guid.NewGuid(), OrderNumber = "K-CF-OLD", CreatedAtUtc = older, UpdatedAtUtc = older, DueAtUtc = older.AddDays(1) },
                new KitchenCustomOrder { Id = Guid.NewGuid(), CustomerId = Guid.NewGuid(), OrderNumber = "K-CF-NEW", CreatedAtUtc = newer, UpdatedAtUtc = newer, DueAtUtc = newer.AddDays(1) });
            await db.SaveChangesAsync();
        }

        var orders = await new KitchenSyncService(new HttpClient(new OfflineHandler()), store).GetCustomOrdersAsync();

        Assert.Equal(new[] { "K-CF-NEW", "K-CF-OLD" }, orders.Select(x => x.OrderNumber));
    }

    [Fact]
    public async Task Receipts_LoadNewestFirst_WithSqliteDateTimeOffsets()
    {
        var path = Path.Combine(Path.GetTempPath(), "SugarERP-Kitchen-tests", Guid.NewGuid().ToString(), "kitchen.db");
        var store = new KitchenStore(path);
        await store.InitializeAsync();
        var older = DateTimeOffset.UtcNow.AddHours(-1);
        var newer = DateTimeOffset.UtcNow;
        var olderReceiptId = Guid.NewGuid();
        var newerReceiptId = Guid.NewGuid();
        await using (var db = store.Open())
        {
            db.Receipts.AddRange(
                new KitchenReceiptRecord { EventId = Guid.NewGuid(), ReceiptId = olderReceiptId, ShipmentId = Guid.NewGuid(), BranchSiteId = Guid.NewGuid(), Status = "RECEIVED", PayloadJson = "{}", CountedAtUtc = older },
                new KitchenReceiptRecord { EventId = Guid.NewGuid(), ReceiptId = newerReceiptId, ShipmentId = Guid.NewGuid(), BranchSiteId = Guid.NewGuid(), Status = "RECEIVED", PayloadJson = "{}", CountedAtUtc = newer });
            await db.SaveChangesAsync();
        }

        var receipts = await new KitchenSyncService(new HttpClient(new OfflineHandler()), store).GetReceiptsAsync();

        Assert.Equal(new[] { newerReceiptId, olderReceiptId }, receipts.Select(x => x.ReceiptId));
    }

    [Fact]
    public async Task MultipleOfflineKitchenWritesUseDistinctSequencesAndApplyLocally()
    {
        var path = Path.Combine(Path.GetTempPath(), "SugarERP-Kitchen-tests", Guid.NewGuid().ToString(), "kitchen.db");
        var store = new KitchenStore(path);
        await store.InitializeAsync();
        var ingredientId = Guid.NewGuid();
        await using (var db = store.Open())
        {
            db.Configuration.Add(new KitchenConfiguration { SiteId = Guid.NewGuid(), DeviceId = Guid.NewGuid(), ProtectedCredential = "offline" });
            db.Ingredients.Add(new KitchenIngredientBalance { ItemId = ingredientId, Name = "سكر", Unit = "جرام", QuantityScale = 1000 });
            await db.SaveChangesAsync();
        }
        var service = new KitchenSyncService(new HttpClient(new OfflineHandler()), store);

        await service.ReceiveIngredientsAsync(new Dictionary<Guid, long> { [ingredientId] = 5000 }, "استلام صباحي");
        await service.ReceiveIngredientsAsync(new Dictionary<Guid, long> { [ingredientId] = 3000 }, "استلام إضافي");

        await using var read = store.Open();
        Assert.Equal(8000, (await read.Ingredients.SingleAsync()).QuantityScaled);
        var sequences = await read.Outbox.OrderBy(x => x.Sequence).Select(x => x.Sequence).ToArrayAsync();
        Assert.Equal(new[] { 1, 2 }, sequences);
        Assert.All(await read.Outbox.ToArrayAsync(), x => Assert.True(x.AppliedLocally));
        Assert.Equal(3, (await read.Configuration.SingleAsync()).NextDeviceSequence);
    }

    [Fact]
    public async Task OfflineKitchenSyncKeepsOutboxAndSchedulesAutomaticRetry()
    {
        var path = Path.Combine(Path.GetTempPath(), "SugarERP-Kitchen-tests", Guid.NewGuid().ToString(), "kitchen.db");
        var store = new KitchenStore(path);
        await store.InitializeAsync();
        var ingredientId = Guid.NewGuid();
        await using (var db = store.Open())
        {
            db.Configuration.Add(new KitchenConfiguration
            {
                ApiBaseUrl = "https://offline.test/api/v1",
                SiteId = Guid.NewGuid(),
                DeviceId = Guid.NewGuid(),
                ProtectedCredential = DeviceCredentialProtector.Protect("device-secret"),
                StreamEpoch = 1
            });
            db.Ingredients.Add(new KitchenIngredientBalance { ItemId = ingredientId, Name = "سكر", Unit = "جرام", QuantityScale = 1000 });
            await db.SaveChangesAsync();
        }
        var service = new KitchenSyncService(new HttpClient(new OfflineHandler()), store);
        await service.ReceiveIngredientsAsync(new Dictionary<Guid, long> { [ingredientId] = 1000 }, "استلام محفوظ دون إنترنت");

        await Assert.ThrowsAsync<HttpRequestException>(() => service.PullAsync(forceRetry: true));

        await using var verify = store.Open();
        var pending = await verify.Outbox.SingleAsync();
        Assert.False(pending.Acknowledged);
        Assert.False(pending.PermanentlyFailed);
        Assert.Equal("OFFLINE", pending.LastErrorCode);
        Assert.Equal(1, pending.Attempts);
        Assert.True(pending.NextAttemptAtUtc > DateTimeOffset.UtcNow);
        Assert.Equal(1000, (await verify.Ingredients.SingleAsync()).QuantityScaled);
    }

    [Fact]
    public async Task TemporaryPushFailureRecoversWithoutDuplicateLocalWrite()
    {
        var path = Path.Combine(Path.GetTempPath(), "SugarERP-Kitchen-tests", Guid.NewGuid().ToString(), "kitchen.db");
        var store = new KitchenStore(path); await store.InitializeAsync();
        var ingredientId = Guid.NewGuid();
        await using (var db = store.Open())
        {
            db.Configuration.Add(new KitchenConfiguration { ApiBaseUrl = "https://sync.test/api/v1", SiteId = Guid.NewGuid(),
                DeviceId = Guid.NewGuid(), ProtectedCredential = DeviceCredentialProtector.Protect("device-secret"), StreamEpoch = 1 });
            db.Ingredients.Add(new KitchenIngredientBalance { ItemId = ingredientId, Name = "سكر", Unit = "g",
                QuantityScale = 1, Active = true, QuantityScaled = 0 });
            await db.SaveChangesAsync();
        }
        var handler = new FlakyKitchenHandler();
        var service = new KitchenSyncService(new HttpClient(handler), store);
        await service.ReceiveIngredientsAsync(new Dictionary<Guid, long> { [ingredientId] = 1_000 }, "temporary outage test");

        Assert.Equal(1, await service.PullAsync()); // authoritative bootstrap refresh completed
        await using (var afterFailure = store.Open())
        {
            Assert.False((await afterFailure.Outbox.SingleAsync()).Acknowledged);
            Assert.Equal(1_000, (await afterFailure.Ingredients.SingleAsync()).QuantityScaled);
        }

        Assert.Equal(0, await service.PullAsync(forceRetry: true));
        await using var afterRecovery = store.Open();
        Assert.True((await afterRecovery.Outbox.SingleAsync()).Acknowledged);
        Assert.Equal(1_000, (await afterRecovery.Ingredients.SingleAsync()).QuantityScaled);
        Assert.Single(await afterRecovery.IngredientMovements.Where(x => x.Kind == "RECEIPT").ToListAsync());
        Assert.Equal(2, handler.PushAttempts);
    }

    [Fact]
    public async Task UpgradeAdvancesSequencePastExistingPendingRows()
    {
        var path = Path.Combine(Path.GetTempPath(), "SugarERP-Kitchen-tests", Guid.NewGuid().ToString(), "kitchen.db");
        var store = new KitchenStore(path); await store.InitializeAsync();
        await using (var db = store.Open())
        {
            db.Configuration.Add(new KitchenConfiguration { SiteId = Guid.NewGuid(), DeviceId = Guid.NewGuid(), NextDeviceSequence = 1 });
            db.Outbox.Add(new KitchenOutbox { EventId = Guid.NewGuid(), RequestId = Guid.NewGuid(), Sequence = 7, UploadJson = "{}" });
            await db.SaveChangesAsync();
        }
        await new KitchenStore(path).InitializeAsync();
        await using var read = store.Open();
        Assert.Equal(8, (await read.Configuration.SingleAsync()).NextDeviceSequence);
    }

    [Fact]
    public async Task UpgradeSeparatesLegacyIngredientFromSellableProductsWithoutChangingStock()
    {
        var path = Path.Combine(Path.GetTempPath(), "SugarERP-Kitchen-tests", Guid.NewGuid().ToString(), "kitchen.db");
        var store = new KitchenStore(path); await store.InitializeAsync();
        var ingredientId = Guid.NewGuid();
        await using (var db = store.Open())
        {
            db.Products.Add(new KitchenProduct { Id = ingredientId, Sku = "FLOUR-1", Name = "Flour", Unit = "g",
                Kind = "INGREDIENT", QuantityScale = 1000, Active = true, Version = 7, UpdatedAtUtc = DateTimeOffset.UtcNow });
            db.Ingredients.Add(new KitchenIngredientBalance { ItemId = ingredientId, Name = "old", Unit = "g",
                QuantityScale = 1, QuantityScaled = 25_000, InventoryCostMinor = 90_000, Version = 1 });
            await db.SaveChangesAsync();
            // Simulate a pre-overhaul database whose schema existed before the
            // product/ingredient separation migration was recorded.
            await db.Database.ExecuteSqlRawAsync("DELETE FROM kitchen_schema_migrations WHERE Version = 2026092303");
        }

        await new KitchenStore(path).InitializeAsync();

        await using var verify = store.Open();
        var ingredient = await verify.Ingredients.SingleAsync();
        Assert.Equal("FLOUR-1", ingredient.Sku);
        Assert.Equal("Flour", ingredient.Name);
        Assert.Equal(1000, ingredient.QuantityScale);
        Assert.Equal(25_000, ingredient.QuantityScaled);
        Assert.Equal(90_000, ingredient.InventoryCostMinor);
        Assert.Equal(7, ingredient.Version);
        Assert.False((await verify.Products.SingleAsync()).Active);
    }

    [Fact]
    public async Task ConfirmAndSendFinalizesReducedShipmentAndCannotDuplicateIt()
    {
        var path = Path.Combine(Path.GetTempPath(), "SugarERP-Kitchen-tests", Guid.NewGuid().ToString(), "kitchen.db");
        var store = new KitchenStore(path); await store.InitializeAsync();
        var requestId = Guid.NewGuid();
        var requestLineId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var ingredientId = Guid.NewGuid();
        await using (var db = store.Open())
        {
            db.Configuration.Add(new KitchenConfiguration { SiteId = Guid.NewGuid(), DeviceId = Guid.NewGuid(), NextDeviceSequence = 1 });
            db.Ingredients.Add(new KitchenIngredientBalance { ItemId = ingredientId, Name = "سكر", Unit = "جرام", QuantityScale = 1, QuantityScaled = 20, Version = 1 });
            db.Recipes.Add(new KitchenRecipeRecord
            {
                ProductItemId = productId,
                OutputScaled = 1,
                Version = 1,
                Components = [new KitchenRecipeComponentRecord { Id = Guid.NewGuid(), ProductItemId = productId, IngredientItemId = ingredientId, QuantityScaled = 2 }]
            });
            db.Requests.Add(new KitchenRequestRecord
            {
                Id = requestId,
                BranchSiteId = Guid.NewGuid(),
                BranchName = "Test Branch",
                Status = "RECEIVED",
                Version = 2,
                SubmittedAtUtc = DateTimeOffset.UtcNow,
                Lines = [new KitchenRequestLineRecord { Id = requestLineId, RequestId = requestId, ItemId = productId, Name = "جاتوه", Unit = "قطعة", QuantityScale = 1, RequestedScaled = 5 }]
            });
            await db.SaveChangesAsync();
        }
        var service = new KitchenSyncService(new HttpClient(new OfflineHandler()), store);

        await service.DispatchAsync(requestId, new Dictionary<Guid, long> { [requestLineId] = 3 });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DispatchAsync(requestId, new Dictionary<Guid, long> { [requestLineId] = 1 }));

        await using var verify = store.Open();
        Assert.Equal("FULFILLED", (await verify.Requests.SingleAsync()).Status);
        Assert.Equal(14, (await verify.Ingredients.SingleAsync()).QuantityScaled);
        var queued = await verify.Outbox.SingleAsync();
        using var upload = JsonDocument.Parse(queued.UploadJson);
        var payload = upload.RootElement.GetProperty("payload");
        Assert.True(payload.GetProperty("finalized").GetBoolean());
        Assert.Equal(3, payload.GetProperty("lines")[0].GetProperty("sent_scaled").GetInt64());
    }

    [Fact]
    public async Task AutomaticPullQueuesAndUploadsKitchenReceiptAcknowledgementExactlyOnce()
    {
        var path = Path.Combine(Path.GetTempPath(), "SugarERP-Kitchen-tests", Guid.NewGuid().ToString(), "kitchen.db");
        var store = new KitchenStore(path); await store.InitializeAsync();
        var kitchenSiteId = Guid.NewGuid();
        var kitchenDeviceId = Guid.NewGuid();
        var branchSiteId = Guid.NewGuid();
        var branchDeviceId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var requestLineId = Guid.NewGuid();
        var requestEventId = Guid.NewGuid();
        await using (var db = store.Open())
        {
            db.Configuration.Add(new KitchenConfiguration
            {
                ApiBaseUrl = "https://sync.test/api/v1",
                SiteId = kitchenSiteId,
                DeviceId = kitchenDeviceId,
                ProtectedCredential = DeviceCredentialProtector.Protect("device-secret"),
                StreamEpoch = 1
            });
            await db.SaveChangesAsync();
        }
        var handler = new KitchenFlowHandler(branchSiteId, branchDeviceId, requestId, requestLineId, requestEventId);
        var service = new KitchenSyncService(new HttpClient(handler), store);

        Assert.Equal(2, await service.PullAsync()); // bootstrap refresh plus the branch request

        await using var verify = store.Open();
        var saved = await verify.Requests.Include(value => value.Lines).SingleAsync();
        Assert.Equal("RECEIVED", saved.Status);
        Assert.Equal("Test Branch", saved.BranchName);
        var acknowledgement = await verify.Outbox.SingleAsync();
        Assert.True(acknowledgement.Acknowledged);
        Assert.Equal(1, acknowledgement.Sequence);
        Assert.Equal(1, handler.AcknowledgementPushes);
        using var upload = JsonDocument.Parse(handler.LastPushBody!);
        var pushed = upload.RootElement.GetProperty("events")[0];
        Assert.Equal("kitchen_request.received", pushed.GetProperty("event_type").GetString());
        Assert.Equal(requestId, pushed.GetProperty("payload").GetProperty("request_id").GetGuid());
        Assert.Equal(branchSiteId, pushed.GetProperty("payload").GetProperty("destination_site_id").GetGuid());
    }

    [Fact]
    public async Task PullAppliesRecipeAndCafeEventsByCanonicalIds()
    {
        var path = Path.Combine(Path.GetTempPath(), "SugarERP-Kitchen-tests", Guid.NewGuid().ToString(), "kitchen.db");
        var store = new KitchenStore(path); await store.InitializeAsync();
        var siteId = Guid.NewGuid(); var deviceId = Guid.NewGuid();
        var productId = Guid.NewGuid(); var ingredientId = Guid.NewGuid(); var customerId = Guid.NewGuid();
        await using (var db = store.Open())
        {
            db.Configuration.Add(new KitchenConfiguration { ApiBaseUrl = "https://sync.test/api/v1", SiteId = siteId,
                DeviceId = deviceId, ProtectedCredential = DeviceCredentialProtector.Protect("device-secret"), StreamEpoch = 1 });
            await db.SaveChangesAsync();
        }
        var service = new KitchenSyncService(new HttpClient(new KitchenReferenceDataHandler(siteId, productId, ingredientId, customerId)), store);

        Assert.Equal(4, await service.PullAsync()); // bootstrap refresh plus three authoritative events

        await using var verify = store.Open();
        var recipe = await verify.Recipes.Include(x => x.Components).SingleAsync();
        Assert.Equal(productId, recipe.ProductItemId);
        Assert.Equal(ingredientId, recipe.Components.Single().IngredientItemId);
        Assert.Equal(200, recipe.Components.Single().QuantityScaled);
        var customer = await verify.CafeCustomers.Include(x => x.Prices).SingleAsync();
        Assert.Equal(customerId, customer.Id);
        Assert.False(customer.Active);
        Assert.True(customer.HiddenLocally);
        Assert.Equal(2, customer.Version);
        Assert.Equal(productId, customer.Prices.Single().ItemId);
    }

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }

    private sealed class KitchenEnrollmentHandler(Guid siteId, Guid deviceId) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/enrollment", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
                {
                    device = new { id = deviceId, siteId, profile = "KITCHEN", streamEpoch = 1 },
                    credential = "new-device-secret", contractVersion = "1.0"
                }) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private sealed class KitchenReferenceDataHandler(Guid siteId, Guid productId, Guid ingredientId, Guid customerId) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/sync/bootstrap", StringComparison.Ordinal))
                return Task.FromResult(Ok(new { contract_version = "1.0", site = new { id = siteId, name = "Test Kitchen", type = "KITCHEN", timezone = "Africa/Cairo" },
                    catalog = new object[] {
                        new { id = productId, sku = "P-1", nameAr = "Cake", unit = "piece", quantityScale = 1, retailPriceMinor = 1000, kind = "PRODUCT", active = true, version = 1 },
                        new { id = ingredientId, sku = "I-1", nameAr = "Flour", unit = "g", quantityScale = 1, retailPriceMinor = 0, kind = "INGREDIENT", active = true, version = 1 }
                    }, customers = Array.Empty<object>(), recipes = Array.Empty<object>(), stock = Array.Empty<object>(), as_of = DateTimeOffset.UtcNow }));
            if (path.EndsWith("/sync/pull", StringComparison.Ordinal))
            {
                var recipe = JsonSerializer.SerializeToElement(new { product_item_id = productId, output_scaled = "1", version = 1,
                    components = new[] { new { ingredient_item_id = ingredientId, quantity_scaled = "200" } } });
                var created = JsonSerializer.SerializeToElement(new { customer_id = customerId, site_id = siteId, name = "Test Cafe", phone = "", notes = "", version = 1,
                    prices = new[] { new { item_id = productId, unit_price_minor = 1000 } } });
                var archived = JsonSerializer.SerializeToElement(new { customer_id = customerId, site_id = siteId, version = 2 });
                var events = new[] { Incoming("recipe.updated", recipe, 1), Incoming("cafe_customer.created", created, 2), Incoming("cafe_customer.archived", archived, 3) };
                return Task.FromResult(Ok(new { contract_version = "1.0", compatibility = new { minimum = "1.0", current = "1.0" }, cursor = "reference-cursor", has_more = false, events }));
            }
            if (path.EndsWith("/sync/ack", StringComparison.Ordinal))
                return Task.FromResult(Ok(new { acknowledged = true, server_position = "3" }));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private JsonElement Incoming(string eventType, JsonElement payload, int position)
        {
            var eventId = Guid.NewGuid(); const string occurredAt = "2026-09-23T10:00:00.000Z";
            return JsonSerializer.SerializeToElement(new { id = eventId, origin_device_id = Guid.NewGuid(), origin_site_id = siteId,
                stream_epoch = 1, device_sequence = position, event_type = eventType, schema_version = 1, occurred_at = occurredAt,
                received_at = "2026-09-23T10:00:01.000Z", payload, dependencies = Array.Empty<Guid>(),
                content_hash = ContractEventFactory.ComputeHash(eventId, position, eventType, 1, occurredAt, payload, []), server_position = position.ToString() });
        }

        private static HttpResponseMessage Ok<T>(T body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }

    private sealed class FlakyKitchenHandler : HttpMessageHandler
    {
        public int PushAttempts { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/sync/push", StringComparison.Ordinal))
            {
                PushAttempts++;
                if (PushAttempts == 1) throw new HttpRequestException("temporary network loss");
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var eventId = body.RootElement.GetProperty("events")[0].GetProperty("id").GetGuid();
                return Ok(new { contract_version = "1.0", next_expected_sequence = 2,
                    results = new[] { new { id = eventId, status = "accepted", server_position = "1", code = (string?)null, error_code = (string?)null, retryable = (bool?)null } } });
            }
            if (path.EndsWith("/sync/bootstrap", StringComparison.Ordinal))
                return Ok(new { contract_version = "1.0", site = new { id = Guid.NewGuid(), name = "Test Kitchen", type = "KITCHEN", timezone = "Africa/Cairo" },
                    catalog = Array.Empty<object>(), customers = Array.Empty<object>(), recipes = Array.Empty<object>(), stock = Array.Empty<object>(), as_of = DateTimeOffset.UtcNow });
            if (path.EndsWith("/sync/pull", StringComparison.Ordinal))
                return Ok(new { contract_version = "1.0", compatibility = new { minimum = "1.0", current = "1.0" }, cursor = "reconnect-cursor", has_more = false, events = Array.Empty<object>() });
            if (path.EndsWith("/sync/ack", StringComparison.Ordinal)) return Ok(new { acknowledged = true, server_position = "0" });
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
        private static HttpResponseMessage Ok<T>(T body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }

    private sealed class KitchenFlowHandler(
        Guid branchSiteId,
        Guid branchDeviceId,
        Guid requestId,
        Guid requestLineId,
        Guid requestEventId) : HttpMessageHandler
    {
        public int AcknowledgementPushes { get; private set; }
        public string? LastPushBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/sync/bootstrap", StringComparison.Ordinal))
                return Ok(new { contract_version = "1.0", site = new { id = Guid.NewGuid(), name = "Test Kitchen", type = "KITCHEN", timezone = "Africa/Cairo" },
                    catalog = Array.Empty<object>(), customers = Array.Empty<object>(), recipes = Array.Empty<object>(), stock = Array.Empty<object>(), as_of = DateTimeOffset.UtcNow });
            if (path.EndsWith("/sync/pull", StringComparison.Ordinal))
            {
                const string occurredAt = "2026-09-21T10:00:00.000Z";
                var payload = JsonSerializer.SerializeToElement(new
                {
                    request_id = requestId,
                    branch_name = "Test Branch",
                    version = 1,
                    lines = new[] { new { line_id = requestLineId, item_id = Guid.NewGuid(), name_snapshot = "جاتوه", unit_snapshot = "قطعة", quantity_scale = 1, requested_scaled = 5 } }
                });
                var incoming = new
                {
                    id = requestEventId,
                    origin_device_id = branchDeviceId,
                    origin_site_id = branchSiteId,
                    stream_epoch = 1,
                    device_sequence = 1,
                    event_type = "kitchen_request.submitted",
                    schema_version = 1,
                    occurred_at = occurredAt,
                    received_at = "2026-09-21T10:00:01.000Z",
                    payload,
                    dependencies = Array.Empty<Guid>(),
                    content_hash = ContractEventFactory.ComputeHash(requestEventId, 1, "kitchen_request.submitted", 1,
                        occurredAt, payload, []),
                    server_position = "1"
                };
                return Ok(new { contract_version = "1.0", compatibility = new { minimum = "1.0", current = "1.0" }, cursor = "test-cursor", has_more = false, events = new[] { incoming } });
            }
            if (path.EndsWith("/sync/ack", StringComparison.Ordinal))
                return Ok(new { acknowledged = true, server_position = "1" });
            if (path.EndsWith("/sync/push", StringComparison.Ordinal))
            {
                LastPushBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                using var body = JsonDocument.Parse(LastPushBody);
                var eventId = body.RootElement.GetProperty("events")[0].GetProperty("id").GetGuid();
                AcknowledgementPushes++;
                return Ok(new { contract_version = "1.0", next_expected_sequence = 2,
                    results = new[] { new { id = eventId, status = "accepted", server_position = "2", code = (string?)null, error_code = (string?)null, retryable = (bool?)null } } });
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Ok<T>(T body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }
}
