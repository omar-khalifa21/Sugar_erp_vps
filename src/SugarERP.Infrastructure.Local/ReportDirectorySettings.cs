using System.Text.Json;
using Microsoft.Win32;

namespace SugarERP.Infrastructure.Local;

public sealed class ReportDirectorySettings
{
    private readonly string _product;
    private readonly string _settingsPath;
    public ReportDirectorySettings(string product)
    {
        if (product is not ("Branch1" or "Branch2" or "Kitchen")) throw new ArgumentException("Unknown product", nameof(product));
        _product = product;
        _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sugar ERP", product, "reports-settings.json");
    }
    public string GetDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            using var installed = Registry.CurrentUser.OpenSubKey($@"Software\Sugar ERP\{_product}");
            if (installed?.GetValue("ReportsDirectory") is string installedPath && !string.IsNullOrWhiteSpace(installedPath)) return installedPath;
        }
        if (File.Exists(_settingsPath))
        {
            var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_settingsPath));
            if (settings?.TryGetValue("reportsDirectory", out var value) == true && !string.IsNullOrWhiteSpace(value)) return value;
        }
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Sugar ERP\{_product}");
            if (key?.GetValue("ReportsDirectory") is string path && !string.IsNullOrWhiteSpace(path)) return path;
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sugar ERP", _product, "Reports");
    }
    public void SaveDirectory(string directory)
    {
        var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(directory.Trim()));
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var programFiles = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(programFiles) && (path.Equals(programFiles, StringComparison.OrdinalIgnoreCase) || path.StartsWith(programFiles + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("لا تحفظ التقارير داخل Program Files.");
        }
        Directory.CreateDirectory(path);
        var probe = Path.Combine(path, $".sugar-write-check-{Guid.NewGuid():N}");
        using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var temporary = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new { reportsDirectory = path }));
        File.Move(temporary, _settingsPath, true);
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Sugar ERP\{_product}");
            key.SetValue("ReportsDirectory", path);
        }
    }
}
