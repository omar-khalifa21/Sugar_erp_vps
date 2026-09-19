using Avalonia;
namespace SugarERP.Branch2.App;
internal static class Program
{
    internal static bool IsSecondInstance { get; private set; }
    [STAThread] public static void Main(string[] args)
    {
        using var instance = new Mutex(true, "Local\\SugarERP.Branch2", out var first);
        IsSecondInstance = !first;
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace().StartWithClassicDesktopLifetime(args);
        GC.KeepAlive(instance);
    }
}
