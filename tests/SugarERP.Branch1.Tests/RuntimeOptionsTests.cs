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
    public void IsolatedDemoPathIsOnlyUsedWithDemoFlag()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sugar-branch1-test-" + Guid.NewGuid());
        var production = RuntimeOptions.Create(["--demo-data-dir=" + directory]);
        var demo = RuntimeOptions.Create(["--demo", "--demo-data-dir=" + directory]);
        Assert.NotEqual(directory, production.DataDirectory);
        Assert.Equal(directory, demo.DataDirectory);
    }
}
