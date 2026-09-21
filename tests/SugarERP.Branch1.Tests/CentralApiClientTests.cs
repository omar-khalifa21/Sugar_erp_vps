using System.Net;
using System.Text;
using System.Text.Json;
using SugarERP.Application;
using SugarERP.Domain;
using SugarERP.Sync.Client;
using Xunit;

namespace SugarERP.Branch1.Tests;

public sealed class CentralApiClientTests
{
    [Fact]
    public async Task BranchEndpoints_UseContractV1PathsBodiesAndDeviceHeaders()
    {
        var deviceId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var shiftId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/health" or "/api/v1/ready" => Json(HttpStatusCode.OK, "{}"),
            "/api/v1/enrollment" => Json(HttpStatusCode.OK, $$"""
                {"device":{"id":"{{deviceId}}","siteId":"{{siteId}}","profile":"BRANCH_TYPE_1","streamEpoch":7},"credential":"issued-secret","contractVersion":"1.0"}
                """),
            "/api/v1/sync/push" => Json(HttpStatusCode.OK, $$"""
                {"contract_version":"1.0","next_expected_sequence":2,"results":[{"id":"{{eventId}}","status":"accepted","server_position":"10","code":null,"error_code":null,"retryable":null}]}
                """),
            "/api/v1/sync/pull" => Json(HttpStatusCode.OK, """
                {"contract_version":"1.0","compatibility":{"minimum":"1.0","current":"1.0"},"cursor":"cursor:next","has_more":false,"events":[]}
                """),
            "/api/v1/sync/ack" => Json(HttpStatusCode.OK, """
                {"acknowledged":true,"server_position":"10"}
                """),
            "/api/v1/reports/upload" => Json(HttpStatusCode.OK, $$"""
                {"status":"accepted","id":"{{reportId}}","uploaded_at":"2026-09-21T08:00:00Z"}
                """),
            _ => Json(HttpStatusCode.NotFound, "{}")
        });
        using var httpClient = new HttpClient(handler);
        var client = new CentralApiClient(httpClient);
        var apiBase = new Uri("https://erp.example.test/api/v1");

        Assert.True((await client.GetHealthAsync(apiBase)).Reachable);
        Assert.True((await client.GetReadyAsync(apiBase)).Reachable);
        var enrollment = await client.EnrollAsync(new EnrollmentCommand(
            apiBase,
            "  one-time-token  ",
            "  POS-01  ",
            "ABCDEF",
            "0.2.0",
            true));
        Assert.Equal(siteId, enrollment.SiteId);
        Assert.Equal(deviceId, enrollment.DeviceId);
        Assert.Equal(DeviceProfile.BranchType1, enrollment.Profile);
        Assert.Equal("issued-secret", enrollment.Credential);
        Assert.Equal(7, enrollment.StreamEpoch);

        var connection = new DeviceConnection(apiBase, deviceId, "device-secret");
        using var payloadDocument = JsonDocument.Parse("{\"sale_id\":\"00000000-0000-0000-0000-000000000001\"}");
        await client.PushAsync(connection, 7,
        [
            new SyncUploadEvent(
                eventId,
                1,
                "sale.completed",
                1,
                "2026-09-10T10:00:00.0000000+00:00",
                payloadDocument.RootElement.Clone(),
                [],
                new string('a', 64))
        ]);
        await client.PullAsync(connection, "a+b/c==", 37);
        await client.AcknowledgeAsync(connection, "cursor:next");
        var reportBytes = new byte[] { 0x50, 0x4b, 0x03, 0x04, 1, 2, 3 };
        var reportHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(reportBytes));
        var reportResult = await client.UploadShiftReportAsync(
            connection, shiftId, 1, "2026-09-21", "MORNING", "Main_Branch_2026-09-21_morning.xlsx", reportHash, reportBytes);
        Assert.Equal(reportId, reportResult.Id);

        Assert.Equal(
        [
            "GET https://erp.example.test/api/v1/health",
            "GET https://erp.example.test/api/v1/ready",
            "POST https://erp.example.test/api/v1/enrollment",
            "POST https://erp.example.test/api/v1/sync/push",
            "GET https://erp.example.test/api/v1/sync/pull?cursor=a%2Bb%2Fc%3D%3D&limit=37",
            "POST https://erp.example.test/api/v1/sync/ack",
            "POST https://erp.example.test/api/v1/reports/upload"
        ], handler.Requests.Select(value => $"{value.Method} {value.Uri}").ToArray());

        var enrollmentRequest = handler.Requests[2];
        Assert.False(enrollmentRequest.Headers.ContainsKey("X-Device-Id"));
        using (var body = JsonDocument.Parse(enrollmentRequest.Body!))
        {
            Assert.Equal("one-time-token", body.RootElement.GetProperty("token").GetString());
            Assert.Equal("POS-01", body.RootElement.GetProperty("deviceName").GetString());
            Assert.Equal("abcdef", body.RootElement.GetProperty("keyThumbprint").GetString());
            Assert.Equal("0.2.0", body.RootElement.GetProperty("appVersion").GetString());
        }

        foreach (var request in handler.Requests.Skip(3))
        {
            Assert.Equal(deviceId.ToString("D"), Assert.Single(request.Headers["X-Device-Id"]));
            Assert.Equal("device-secret", Assert.Single(request.Headers["X-Device-Secret"]));
            Assert.Equal("application/json", Assert.Single(request.Headers["Accept"]));
        }
        using (var body = JsonDocument.Parse(handler.Requests[3].Body!))
        {
            Assert.Equal("1.0", body.RootElement.GetProperty("contract_version").GetString());
            Assert.Equal(7, body.RootElement.GetProperty("stream_epoch").GetInt32());
            Assert.Equal(eventId, body.RootElement.GetProperty("events")[0].GetProperty("id").GetGuid());
        }
        using (var body = JsonDocument.Parse(handler.Requests[5].Body!))
            Assert.Equal("cursor:next", body.RootElement.GetProperty("cursor").GetString());
        using (var body = JsonDocument.Parse(handler.Requests[6].Body!))
        {
            Assert.Equal(shiftId, body.RootElement.GetProperty("shiftId").GetGuid());
            Assert.Equal(reportHash, body.RootElement.GetProperty("contentHash").GetString());
            Assert.Equal(reportBytes.Length, body.RootElement.GetProperty("byteLength").GetInt32());
            Assert.Equal(reportBytes, Convert.FromBase64String(body.RootElement.GetProperty("contentBase64").GetString()!));
        }
    }

    [Fact]
    public async Task Bootstrap_UsesDeviceAuthenticationAndReturnsCatalogSnapshot()
    {
        var deviceId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, $$"""
            {"contract_version":"1.0","site":{"id":"{{siteId}}"},"catalog":[{"id":"{{itemId}}","sku":"AUTO-{{itemId}}","nameAr":"صنف جديد","unit":"قطعة","quantityScale":1,"retailPriceMinor":1000,"active":true,"version":1}]}
            """));
        using var httpClient = new HttpClient(handler);

        var result = await new CentralApiClient(httpClient).GetBootstrapAsync(
            new DeviceConnection(new Uri("https://erp.example.test/api/v1"), deviceId, "secret"));

        Assert.Equal(itemId, result.GetProperty("catalog")[0].GetProperty("id").GetGuid());
        var request = Assert.Single(handler.Requests);
        Assert.Equal("GET https://erp.example.test/api/v1/sync/bootstrap", $"{request.Method} {request.Uri}");
        Assert.Equal(deviceId.ToString("D"), Assert.Single(request.Headers["X-Device-Id"]));
        Assert.Equal("secret", Assert.Single(request.Headers["X-Device-Secret"]));
    }

    [Theory]
    [InlineData("http://erp.example.test/api/v1")]
    [InlineData("https://erp.example.test/api/v2")]
    [InlineData("https://erp.example.test/")]
    public void BuildEndpoint_RejectsInsecureOrUnversionedProductionUrls(string value)
    {
        Assert.Throws<ArgumentException>(() => CentralApiClient.BuildEndpoint(new Uri(value), "health"));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string value) => new(status)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method.Method,
                request.RequestUri!.AbsoluteUri,
                request.Headers.ToDictionary(
                    value => value.Key,
                    value => value.Value.ToArray(),
                    StringComparer.OrdinalIgnoreCase),
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return responseFactory(request);
        }
    }

    private sealed record RecordedRequest(
        string Method,
        string Uri,
        IReadOnlyDictionary<string, string[]> Headers,
        string? Body);
}
