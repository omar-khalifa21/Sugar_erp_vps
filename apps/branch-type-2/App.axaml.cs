using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SugarERP.Infrastructure.Local;
namespace SugarERP.Branch2.App;
public sealed class App : Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Program.IsSecondInstance)
            {
                desktop.MainWindow = new Avalonia.Controls.Window { Title = "Sugar ERP", Width = 480, Height = 180,
                    Content = new Avalonia.Controls.TextBlock { Text = "البرنامج مفتوح بالفعل. استخدم النافذة المفتوحة لحماية معاملات المخزون.", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Avalonia.Thickness(24) } };
                base.OnFrameworkInitializationCompleted(); return;
            }
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sugar ERP", "Branch2");
            var database = new LocalDatabase(Path.Combine(root, "branch.db"));
            database.InitializeAsync().GetAwaiter().GetResult();
            desktop.MainWindow = new MainWindow(new Branch2ApplicationService(database, new HttpClient { Timeout = TimeSpan.FromSeconds(30) }));
        }
        base.OnFrameworkInitializationCompleted();
    }
}
