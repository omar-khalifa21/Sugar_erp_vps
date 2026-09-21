using SugarERP.Branch1;
using Xunit;

namespace SugarERP.Branch1.Tests;

public sealed class RuntimeOptionsTests
{
    [Fact]
    public void TouchFlagSelectsTouchWithoutChangingOperationalDataIdentity()
    {
        var desktop = RuntimeOptions.Create([]);
        var touch = RuntimeOptions.Create(["--touch"]);
        Assert.False(desktop.IsTouch);
        Assert.True(touch.IsTouch);
        Assert.Equal(desktop.DatabasePath, touch.DatabasePath);
        Assert.Equal(desktop.InstanceName, touch.InstanceName);
    }

    [Fact]
    public void DemoArgumentsAreRejected()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sugar-branch1-test-" + Guid.NewGuid());
        Assert.Throws<ArgumentException>(() => RuntimeOptions.Create(["--demo"]));
        Assert.Throws<ArgumentException>(() => RuntimeOptions.Create(["--demo-data-dir=" + directory]));
    }

    [Fact]
    public void LegacyDemoCleanerRemovesOnlyDemoDatabaseFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sugar-branch1-cleanup-" + Guid.NewGuid());
        var backups = Path.Combine(directory, "backups");
        Directory.CreateDirectory(backups);
        var production = Path.Combine(directory, "branch-type-1.db");
        var demo = Path.Combine(directory, "branch-type-1-demo.db");
        var demoWal = demo + "-wal";
        var demoBackup = Path.Combine(backups, "branch-type-1-demo-pre-upgrade.db");
        var productionBackup = Path.Combine(backups, "branch-type-1-pre-upgrade.db");
        try
        {
            File.WriteAllText(production, "production");
            File.WriteAllText(demo, "sample");
            File.WriteAllText(demoWal, "sample");
            File.WriteAllText(demoBackup, "sample");
            File.WriteAllText(productionBackup, "production");

            LegacyDemoDataCleaner.Remove(directory);

            Assert.True(File.Exists(production));
            Assert.True(File.Exists(productionBackup));
            Assert.False(File.Exists(demo));
            Assert.False(File.Exists(demoWal));
            Assert.False(File.Exists(demoBackup));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
