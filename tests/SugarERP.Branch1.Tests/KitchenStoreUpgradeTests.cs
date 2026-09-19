using Microsoft.EntityFrameworkCore;
using SugarERP.Kitchen;
using Xunit;

namespace SugarERP.Branch1.Tests;

public sealed class KitchenStoreUpgradeTests
{
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

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }
}
