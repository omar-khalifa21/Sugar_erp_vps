using System.Net;
using System.Security.Cryptography;
using System.Text;
using SugarERP.Desktop.Shared;
using Xunit;

namespace SugarERP.Branch1.Tests;

public sealed class DesktopUpdateServiceTests
{
    [Fact]
    public async Task DetectsNewerCompatibleVersion()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/current")
            ? Json("""{"profile":"KITCHEN","channel":"production","version":"1.4.0","filename":"Sugar-Kitchen-1.4.0.exe","publishedAt":"2026-09-18T00:00:00Z","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","required":false,"signed":true,"downloadUrl":"current/download"}""")
            : throw new InvalidOperationException());
        var service = new DesktopUpdateService(new HttpClient(handler), new(DesktopApplicationType.Kitchen,
            new Uri("https://erp.example/api/v1/"), "1.3.0", "Production"));

        var result = await service.CheckAsync();

        Assert.True(result.UpdateAvailable);
        Assert.Equal("1.4.0", result.Release!.Version);
    }

    [Fact]
    public async Task RejectsWrongProfile()
    {
        var handler = new StubHandler(_ => Json("""{"profile":"BRANCH_TYPE_2","channel":"production","version":"2.0.0","filename":"x.exe","publishedAt":"2026-09-18T00:00:00Z","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","required":false,"signed":true,"downloadUrl":"current/download"}"""));
        var service = new DesktopUpdateService(new HttpClient(handler), new(DesktopApplicationType.Kitchen,
            new Uri("https://erp.example/api/v1/"), "1.0.0", "Production"));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckAsync());
    }

    [Fact]
    public async Task DownloadsAndVerifiesHash()
    {
        var bytes = Encoding.UTF8.GetBytes("signed installer bytes");
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        var service = new DesktopUpdateService(new HttpClient(handler), new(DesktopApplicationType.BranchType1,
            new Uri("https://erp.example/api/v1/"), "1.0.0", "Production"), _ => true);
        var release = new DesktopReleaseManifest("BRANCH_TYPE_1", "production", "1.1.0", "Sugar-Branch-Type-1-1.1.0.exe",
            DateTimeOffset.UtcNow, hash, null, false, true, "current/download");

        var path = await service.DownloadAndVerifyAsync(release);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task RejectsNonProductionReleaseChannel()
    {
        var handler = new StubHandler(_ => Json("""{"profile":"KITCHEN","channel":"preview","version":"2.0.0","filename":"Sugar-Kitchen-2.0.0.exe","publishedAt":"2026-09-18T00:00:00Z","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","required":false,"signed":true,"downloadUrl":"current/download"}"""));
        var service = new DesktopUpdateService(new HttpClient(handler), new(DesktopApplicationType.Kitchen,
            new Uri("https://erp.example/api/v1/"), "1.0.0", "Production"));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckAsync());
    }

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }
}
