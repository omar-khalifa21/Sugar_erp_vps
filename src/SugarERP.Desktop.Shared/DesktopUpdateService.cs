using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SugarERP.Desktop.Shared;

public sealed record DesktopReleaseManifest(
    [property: JsonPropertyName("profile")] string Profile,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("filename")] string Filename,
    [property: JsonPropertyName("publishedAt")] DateTimeOffset PublishedAt,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("releaseNotes")] string? ReleaseNotes,
    [property: JsonPropertyName("required")] bool Required,
    [property: JsonPropertyName("signed")] bool Signed,
    [property: JsonPropertyName("downloadUrl")] string DownloadUrl);

public sealed record UpdateCheckResult(bool UpdateAvailable, DesktopReleaseManifest? Release, string Message);

public sealed class DesktopUpdateService(HttpClient http, DeploymentConfiguration configuration, Func<string, bool>? verifyPublisher = null)
{
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(configuration.UpdateManifestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        var release = await response.Content.ReadFromJsonAsync<DesktopReleaseManifest>(cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("The update manifest is empty.");
        var expectedProfile = configuration.ApplicationType switch
        {
            DesktopApplicationType.BranchType1 => "BRANCH_TYPE_1",
            DesktopApplicationType.BranchType2 => "BRANCH_TYPE_2",
            DesktopApplicationType.Kitchen => "KITCHEN",
            _ => throw new ArgumentOutOfRangeException(),
        };
        if (!string.Equals(release.Profile, expectedProfile, StringComparison.Ordinal))
            throw new InvalidDataException("The update manifest is for another application type.");
        if (!Version.TryParse(configuration.Version, out var installed) || !Version.TryParse(release.Version, out var available))
            throw new InvalidDataException("The installed or available version is invalid.");
        var update = available > installed;
        return new(update, release, update ? $"Update available — Version {release.Version}" : "You're up to date.");
    }

    public async Task<string> DownloadAndVerifyAsync(DesktopReleaseManifest release, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var download = new Uri(configuration.UpdateManifestUrl, release.DownloadUrl);
        if (download.Scheme != configuration.UpdateManifestUrl.Scheme || download.Host != configuration.UpdateManifestUrl.Host)
            throw new InvalidDataException("The update download must use the configured update server.");
        var directory = Path.Combine(Path.GetTempPath(), "Sugar ERP", "Updates", configuration.ApplicationType.ToString(), release.Version);
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, Path.GetFileName(release.Filename));
        using var response = await http.GetAsync(download, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var destination = new FileStream(target + ".download", FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
        {
            var buffer = new byte[81920]; long received = 0; int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken); received += read;
                if (total > 0) progress?.Report((double)received / total.Value);
            }
        }
        string actual;
        await using (var downloaded = File.OpenRead(target + ".download"))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(downloaded, cancellationToken)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(release.Sha256)))
        {
            File.Delete(target + ".download");
            throw new InvalidDataException("The downloaded update checksum is invalid.");
        }
        if (release.Signed && !(verifyPublisher ?? AuthenticodeVerifier.IsTrusted)(target + ".download"))
        {
            File.Delete(target + ".download");
            throw new InvalidDataException("The update publisher signature is invalid or untrusted.");
        }
        File.Move(target + ".download", target, true);
        return target;
    }

    public static void LaunchInstaller(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Desktop updates are installed on Windows.");
        Process.Start(new ProcessStartInfo(path, "/S /Relaunch") { UseShellExecute = true, Verb = "runas" });
    }
}
