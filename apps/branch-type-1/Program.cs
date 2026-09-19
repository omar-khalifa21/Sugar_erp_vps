using Avalonia;

namespace SugarERP.Branch1;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        RuntimeContext.Options = RuntimeOptions.Create(args);
        using var instanceMutex = new Mutex(true, RuntimeContext.Options.InstanceName, out var isFirstInstance);
        RuntimeContext.IsSecondInstance = !isFirstInstance;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        GC.KeepAlive(instanceMutex);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UsePlatformDetect()
        .WithInterFont();
}
