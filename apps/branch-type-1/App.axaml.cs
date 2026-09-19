using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using SugarERP.Desktop.Shared.ViewModels;
using SugarERP.Infrastructure.Local;
using SugarERP.Sync.Client;
using SugarERP.Desktop.Shared;

namespace SugarERP.Branch1;

public sealed partial class App : Avalonia.Application
{
    private HttpClient? _httpClient;
    private BackgroundSyncLoop? _backgroundSync;

    public override void Initialize() => Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (RuntimeContext.IsSecondInstance)
            {
                desktop.MainWindow = BuildSecondInstanceWindow();
            }
            else
            {
                var options = RuntimeContext.Options;
                var database = new LocalDatabase(options.DatabasePath);
                var operations = new BranchOperationsService(database);
                var moduleOperations = new BranchModuleOperationsService(database);
                var shiftReportWriter = new OpenXmlShiftReportWriter();
                var printer = new WindowsRasterBranchPrinter();
                _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                var enrollment = new EnrollmentClient(_httpClient);
                var sync = new BranchSyncService(_httpClient, database);
                var viewModel = new BranchPosViewModel(operations, moduleOperations, shiftReportWriter, printer, enrollment, sync, options.IsDemo,
                    DeploymentConfiguration.Create(DesktopApplicationType.BranchType1).Version);
                desktop.MainWindow = new MainWindow(viewModel, database, _httpClient, options.IsDemo, options.IsTouch);
                if (!options.IsDemo)
                {
                    _backgroundSync = new BackgroundSyncLoop(sync, TimeSpan.FromSeconds(15));
                    desktop.MainWindow.Opened += (_, _) => _backgroundSync.Start();
                    desktop.Exit += async (_, _) =>
                    {
                        if (_backgroundSync is not null) await _backgroundSync.DisposeAsync();
                        _httpClient?.Dispose();
                    };
                }
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static Window BuildSecondInstanceWindow()
    {
        var window = new Window
        {
            Title = "سكر — فرع نوع ١",
            Width = 480,
            Height = 220,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        var close = new Button
        {
            Content = "حسناً",
            HorizontalAlignment = HorizontalAlignment.Center,
            MinWidth = 120,
            MinHeight = 48
        };
        close.Click += (_, _) => window.Close();
        window.Content = new StackPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Spacing = 20,
            Margin = new Thickness(28),
            Children =
            {
                new TextBlock { Text = "البرنامج مفتوح بالفعل", FontSize = 24, FontWeight = Avalonia.Media.FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = "استخدم النافذة المفتوحة حتى لا يعمل جهازان على نفس بيانات المخزون.", TextWrapping = Avalonia.Media.TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center },
                close
            }
        };
        return window;
    }
}
