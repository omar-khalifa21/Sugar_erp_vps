using System.Security.Cryptography;
using System.Text;

namespace SugarERP.Branch1;

public sealed record RuntimeOptions(bool IsTouch, string DataDirectory, string DatabasePath, string InstanceName)
{
    public static RuntimeOptions Create(string[] args)
    {
        if (args.Any(value => string.Equals(value, "--demo", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("--demo-data-dir=", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Demo mode has been removed. Start the production application without demo arguments.");

        var isTouch = args.Any(value => string.Equals(value, "--touch", StringComparison.OrdinalIgnoreCase))
            || File.Exists(Path.Combine(AppContext.BaseDirectory, "touch.variant"));
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
            localData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".sugar-erp");

        var dataDirectory = Path.Combine(localData, "Sugar ERP", "Branch Type 1");
        LegacyDemoDataCleaner.Remove(dataDirectory);
        var databasePath = Path.Combine(dataDirectory, "branch-type-1.db");
        var identity = $"{Environment.UserName}|{Path.GetFullPath(databasePath).ToUpperInvariant()}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        var name = OperatingSystem.IsWindows() ? $"Local\\SugarERP.BranchType1.{hash}" : $"SugarERP.BranchType1.{hash}";
        return new RuntimeOptions(isTouch, dataDirectory, databasePath, name);
    }
}

public static class LegacyDemoDataCleaner
{
    public static void Remove(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        DeleteMatches(dataDirectory, "branch-type-1-demo.db*");
        DeleteMatches(Path.Combine(dataDirectory, "backups"), "branch-type-1-demo-*");
    }

    private static void DeleteMatches(string directory, string pattern)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var path in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
            File.Delete(path);
    }
}

internal static class RuntimeContext
{
    public static RuntimeOptions Options { get; set; } = RuntimeOptions.Create([]);
    public static bool IsSecondInstance { get; set; }
}
