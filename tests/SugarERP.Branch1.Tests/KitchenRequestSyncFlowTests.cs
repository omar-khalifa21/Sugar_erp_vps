using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SugarERP.Application;
using SugarERP.Domain;
using SugarERP.Infrastructure.Local;
using SugarERP.Sync.Client;
using Xunit;

namespace SugarERP.Branch1.Tests;

public sealed class KitchenRequestSyncFlowTests
{
    [Fact]
    public async Task BranchPushKitchenReceiptAndDispatchCompleteTheRequestWithoutDuplicates()
    {
        var path = Path.Combine(Path.GetTempPath(), "SugarERP-BranchSync-tests", Guid.NewGuid().ToString(), "branch.db");
        var database = new LocalDatabase(path);
        await database.InitializeAsync();
        var branchSiteId = Guid.NewGuid();
        var branchDeviceId = Guid.NewGuid();
        var kitchenSiteId = Guid.NewGuid();
        var kitchenDeviceId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        await using (var db = database.CreateContext())
        {
            db.DeviceConfigurations.Add(new DeviceConfiguration
            {
                SiteId = branchSiteId,
                DeviceId = branchDeviceId,
                Profile = DeviceProfile.BranchType1,
                SiteName = "Test Branch",
                ApiBaseUrl = "https://sync.test/api/v1",
                DeviceCredential = DeviceCredentialProtector.Protect("device-secret"),
                StreamEpoch = 1,
                EnrolledAtUtc = DateTimeOffset.UtcNow
            });
            db.CatalogItems.Add(new CatalogItem
            {
                Id = itemId, Sku = "TEST-1", NameAr = "جاتوه", Unit = "قطعة", QuantityScale = 1,
                RetailPriceMinor = 10_000, Active = true, Version = 1, UpdatedAtUtc = DateTimeOffset.UtcNow,
                StockBalance = new StockBalance { ItemId = itemId, Revision = 1, AsOfUtc = DateTimeOffset.UtcNow }
            });
            await db.SaveChangesAsync();
        }
        var modules = new BranchModuleOperationsService(database);
        var created = await modules.CreateKitchenRequestAsync(
            new CreateKitchenRequestCommand(Guid.NewGuid(), [new QuantityInput(itemId, 5)]), true);
        Guid requestEventId;
        Guid requestLineId;
        await using (var db = database.CreateContext())
        {
            requestEventId = await db.OutboxMessages.Where(value => value.AggregateId == created.Id).Select(value => value.EventId).SingleAsync();
            requestLineId = await db.KitchenRequestLines.Where(value => value.RequestId == created.Id).Select(value => value.Id).SingleAsync();
        }
        var handler = new BranchFlowHandler(branchSiteId, branchDeviceId, kitchenSiteId, kitchenDeviceId, itemId, created.Id, requestLineId, requestEventId);

        var result = await new BranchSyncService(new HttpClient(handler), database).SynchronizeAsync();

        await using var debug = database.CreateContext();
        var debugMessage = $"{result.UserMessage} | {string.Join(",", handler.Paths)} | inbox={await debug.InboxMessages.CountAsync()} shipments={await debug.Shipments.CountAsync()} request={(await debug.KitchenRequests.SingleAsync()).Status}";
        Assert.True(result.Succeeded, debugMessage);
        Assert.Equal(1, result.Acknowledged);
        Assert.Equal(2, result.Received);
        var request = Assert.Single(await modules.GetKitchenRequestsAsync(), value => value.Id == created.Id);
        Assert.Equal(KitchenRequestStatus.Fulfilled, request.Status);
        Assert.Equal(RequestDeliveryState.Received, request.DeliveryState);
        Assert.Equal(3, Assert.Single(request.Lines).SentScaled);
        Assert.Single(await modules.GetIncomingShipmentsAsync());

        var replay = await new BranchSyncService(new HttpClient(handler), database).SynchronizeAsync();
        Assert.True(replay.Succeeded, replay.UserMessage);
        Assert.Equal(0, replay.Received);
        Assert.Single(await modules.GetIncomingShipmentsAsync());
    }

    private sealed class BranchFlowHandler : HttpMessageHandler
    {
        private readonly object _bootstrap;
        private readonly object _firstPage;
        private int _pulls;
        public List<string> Paths { get; } = [];

        public BranchFlowHandler(Guid branchSiteId, Guid branchDeviceId, Guid kitchenSiteId, Guid kitchenDeviceId,
            Guid itemId, Guid requestId, Guid requestLineId, Guid requestEventId)
        {
            _bootstrap = new
            {
                contract_version = "1.0",
                site = new { id = branchSiteId, name = "Test Branch", type = "BRANCH_TYPE_1", timezone = "Africa/Cairo" },
                catalog = new[] { new { id = itemId, sku = "TEST-1", nameAr = "جاتوه", unit = "قطعة", quantityScale = 1,
                    retailPriceMinor = 10_000, active = true, version = 1 } },
                customers = Array.Empty<object>(), recipes = Array.Empty<object>(), stock = Array.Empty<object>(), as_of = DateTimeOffset.UtcNow
            };
            // Preserve all seven fractional digits and the explicit offset: this
            // is the exact shape emitted by the Kitchen desktop and hashed by the
            // Branch client before the VPS stores and returns the event.
            var receivedAt = "2026-09-22T12:32:03.3157266+00:00";
            var receivedPayload = Element(new { request_id = requestId, destination_site_id = branchSiteId, status = "RECEIVED", version = 2, received_at = receivedAt });
            var receivedEventId = Guid.NewGuid();
            var receivedHash = ContractEventFactory.ComputeHash(receivedEventId, 1, "kitchen_request.received", 1, receivedAt, receivedPayload, [requestEventId]);
            var dispatchedAt = "2026-09-21T10:02:00.0000000Z";
            var shipmentId = Guid.NewGuid();
            var shipmentPayload = Element(new
            {
                shipment_id = shipmentId,
                request_id = requestId,
                destination_site_id = branchSiteId,
                reference = "K-TEST-1",
                version = 1,
                finalized = true,
                dispatched_at = dispatchedAt,
                lines = new[] { new { line_id = Guid.NewGuid(), request_line_id = requestLineId, item_id = itemId,
                    sent_scaled = 3, quantity_scale = 1, name_snapshot = "جاتوه", unit_snapshot = "قطعة" } }
            });
            var shipmentEventId = Guid.NewGuid();
            var shipmentHash = ContractEventFactory.ComputeHash(shipmentEventId, 2, "shipment.dispatched", 1, dispatchedAt, shipmentPayload, []);
            _firstPage = new
            {
                contract_version = "1.0",
                compatibility = new { minimum = "1.0", current = "1.0" },
                cursor = "cursor-2",
                has_more = false,
                events = new object[]
                {
                    new { id = receivedEventId, origin_device_id = kitchenDeviceId, origin_site_id = kitchenSiteId, stream_epoch = 1,
                        device_sequence = 1, event_type = "kitchen_request.received", schema_version = 1, occurred_at = receivedAt,
                        received_at = receivedAt, payload = receivedPayload, dependencies = new[] { requestEventId }, content_hash = receivedHash, server_position = "1" },
                    new { id = shipmentEventId, origin_device_id = kitchenDeviceId, origin_site_id = kitchenSiteId, stream_epoch = 1,
                        device_sequence = 2, event_type = "shipment.dispatched", schema_version = 1, occurred_at = dispatchedAt,
                        received_at = dispatchedAt, payload = shipmentPayload, dependencies = Array.Empty<Guid>(), content_hash = shipmentHash, server_position = "2" }
                }
            };
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            if (path.EndsWith("/sync/push", StringComparison.Ordinal))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var id = body.RootElement.GetProperty("events")[0].GetProperty("id").GetGuid();
                return Ok(new { contract_version = "1.0", next_expected_sequence = 2,
                    results = new[] { new { id, status = "accepted", server_position = "1", code = (string?)null, error_code = (string?)null, retryable = (bool?)null } } });
            }
            if (path.EndsWith("/sync/bootstrap", StringComparison.Ordinal)) return Ok(_bootstrap);
            if (path.EndsWith("/sync/pull", StringComparison.Ordinal))
                return Ok(Interlocked.Increment(ref _pulls) == 1 ? _firstPage : new
                {
                    contract_version = "1.0", compatibility = new { minimum = "1.0", current = "1.0" },
                    cursor = "cursor-2", has_more = false, events = Array.Empty<object>()
                });
            if (path.EndsWith("/sync/ack", StringComparison.Ordinal)) return Ok(new { acknowledged = true, server_position = "2" });
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        private static HttpResponseMessage Ok<T>(T body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }
}
