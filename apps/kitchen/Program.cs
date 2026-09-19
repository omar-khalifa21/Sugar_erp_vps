using Avalonia;

namespace SugarERP.Kitchen.App;
internal static class Program
{
    internal static bool IsSecondInstance { get; private set; }
    [STAThread] public static void Main(string[] args)
    {
        using var instance = new Mutex(true, "Local\\SugarERP.Kitchen", out var first);
        IsSecondInstance = !first;
        Build().StartWithClassicDesktopLifetime(args);
        GC.KeepAlive(instance);
    }
    private static AppBuilder Build() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
