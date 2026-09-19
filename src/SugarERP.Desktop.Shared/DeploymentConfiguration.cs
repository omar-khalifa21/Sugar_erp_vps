using System.Reflection;

namespace SugarERP.Desktop.Shared;

public enum DesktopApplicationType
{
    BranchType1,
    BranchType2,
    Kitchen,
}

public sealed record DeploymentConfiguration(
    DesktopApplicationType ApplicationType,
    Uri ApiBaseUrl,
    string Version,
    string Environment)
{
    public Uri UpdateManifestUrl => new(ApiBaseUrl.ToString().TrimEnd('/') + "/releases/" + (ApplicationType switch
    {
        DesktopApplicationType.BranchType1 when File.Exists(Path.Combine(AppContext.BaseDirectory, "touch.variant")) => "branch-type-1/touch/current",
        DesktopApplicationType.BranchType1 => "branch-type-1/current",
        DesktopApplicationType.BranchType2 => "branch-type-2/current",
        DesktopApplicationType.Kitchen => "kitchen/current",
        _ => throw new ArgumentOutOfRangeException(nameof(ApplicationType)),
    }));

    public static DeploymentConfiguration Create(DesktopApplicationType applicationType, Assembly? assembly = null)
    {
        assembly ??= Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        var environment = System.Environment.GetEnvironmentVariable("SUGAR_ERP_ENVIRONMENT")?.Trim();
        if (string.IsNullOrWhiteSpace(environment) && metadata.TryGetValue("SugarErpEnvironment", out var builtEnvironment)) environment = builtEnvironment;
        if (string.IsNullOrWhiteSpace(environment)) environment = "Production";

        var configured = System.Environment.GetEnvironmentVariable("SUGAR_ERP_API_BASE_URL")?.Trim();
        var defaultUrl = metadata.TryGetValue("SugarErpApiBaseUrl", out var builtUrl) && !string.IsNullOrWhiteSpace(builtUrl)
            ? builtUrl
            : environment.Equals("Development", StringComparison.OrdinalIgnoreCase) ? "http://localhost:3001/api/v1" : "https://ascendyz.xyz/api/v1";
        var api = new Uri(string.IsNullOrWhiteSpace(configured) ? defaultUrl : configured!, UriKind.Absolute);
        if (!api.IsLoopback && api.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Production API configuration must use HTTPS.");

        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?.Split('+', 2)[0] ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        return new(applicationType, new Uri(api.ToString().TrimEnd('/') + "/"), version, environment);
    }
}
