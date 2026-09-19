using System.Security.Cryptography;
using System.Text;

namespace SugarERP.Branch1;

public sealed record RuntimeOptions(bool IsDemo, bool IsTouch, string DataDirectory, string DatabasePath, string InstanceName)
{
    public static RuntimeOptions Create(string[] args)
    {
        var isDemo = args.Any(value => string.Equals(value, "--demo", StringComparison.OrdinalIgnoreCase));
        var isTouch = args.Any(value => string.Equals(value, "--touch", StringComparison.OrdinalIgnoreCase))
            || File.Exists(Path.Combine(AppContext.BaseDirectory, "touch.variant"));
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
            localData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".sugar-erp");

        var dataDirectory = Path.Combine(localData, "Sugar ERP", "Branch Type 1");
        if (isDemo)
        {
            var isolatedDirectory = args.FirstOrDefault(value => value.StartsWith("--demo-data-dir=", StringComparison.OrdinalIgnoreCase));
            if (isolatedDirectory is not null)
            {
                var specifiedPath = isolatedDirectory["--demo-data-dir=".Length..];
                if (!Path.IsPathFullyQualified(specifiedPath))
                    throw new ArgumentException("Demo data directory must be an absolute path.");
                dataDirectory = Path.GetFullPath(specifiedPath);
            }
        }
        var databasePath = Path.Combine(dataDirectory, isDemo ? "branch-type-1-demo.db" : "branch-type-1.db");
        var identity = $"{Environment.UserName}|{Path.GetFullPath(databasePath).ToUpperInvariant()}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        var name = OperatingSystem.IsWindows() ? $"Local\\SugarERP.BranchType1.{hash}" : $"SugarERP.BranchType1.{hash}";
        return new RuntimeOptions(isDemo, isTouch, dataDirectory, databasePath, name);
    }
}

internal static class RuntimeContext
{
    public static RuntimeOptions Options { get; set; } = RuntimeOptions.Create([]);
    public static bool IsSecondInstance { get; set; }
}
