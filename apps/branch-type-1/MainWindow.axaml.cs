using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SugarERP.Desktop.Shared.ViewModels;
using SugarERP.Desktop.Shared;
using SugarERP.Infrastructure.Local;
using SugarERP.Domain;
using Microsoft.EntityFrameworkCore;

namespace SugarERP.Branch1;

public sealed partial class MainWindow : Window
{
    private readonly BranchPosViewModel _viewModel;
    private bool _compactPos;
    private readonly LocalDatabase? _database;
    private readonly HttpClient? _http;
    private DesktopReleaseManifest? _availableUpdate;
    private readonly DeploymentConfiguration _deployment = DeploymentConfiguration.Create(DesktopApplicationType.BranchType1);
    private readonly CancellationTokenSource _updateStop = new();

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = null!;
        SizeChanged += (_, _) => UpdatePosLayout();
    }

    public MainWindow(BranchPosViewModel viewModel, LocalDatabase database, HttpClient http, bool isTouch = false)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _database = database;
        _http = http;
        if (isTouch) Classes.Add("touch");
        DataContext = viewModel;
        Opened += OnOpened;
        SizeChanged += (_, _) => UpdatePosLayout();
        AddHandler(KeyDownEvent, OnFormKeyDown, RoutingStrategies.Tunnel);
        Closed += (_, _) => _updateStop.Cancel();
    }

    private async void OnOpened(object? sender, EventArgs eventArgs)
    {
        Opened -= OnOpened;
        await _viewModel.InitializeAsync();
        _ = RunPeriodicUpdateChecksAsync(_updateStop.Token);
    }

    private async void Update_Click(object? sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null) { await CheckForUpdatesAsync(true); return; }
        try
        {
            await using var db = _database!.CreateContext();
            if (await db.Shifts.AnyAsync(x => x.Status == ShiftStatus.Open)) throw new InvalidOperationException("Close the shift before updating.");
            var pendingSync = await db.OutboxMessages.AnyAsync(x => x.State != OutboxState.Acknowledged)
                || await db.SideEffectJobs.AnyAsync(x => x.Kind == SideEffectKind.UploadShiftReport && x.State != SideEffectState.Completed);
            if (pendingSync) throw new InvalidOperationException("The update is waiting for all saved changes and shift reports to synchronize.");
            this.FindControl<Button>("UpdateButton")!.Content = "Downloading update...";
            var path = await new DesktopUpdateService(_http!, _deployment).DownloadAndVerifyAsync(_availableUpdate,
                new Progress<double>(x => this.FindControl<Button>("UpdateButton")!.Content = $"Downloading — {x:P0}"));
            await _database.CreateUpdateBackupAsync();
            DesktopUpdateService.LaunchInstaller(path);
            Close();
        }
        catch (Exception ex) { this.FindControl<Button>("UpdateButton")!.Content = $"Update failed — {ex.Message}"; }
    }

    private async Task CheckForUpdatesAsync(bool reportCurrent)
    {
        try
        {
            var result = await new DesktopUpdateService(_http!, _deployment).CheckAsync();
            _availableUpdate = result.UpdateAvailable ? result.Release : null;
            this.FindControl<Button>("UpdateButton")!.Content = result.UpdateAvailable ? result.Message : "Check for Updates";
            if (reportCurrent && !result.UpdateAvailable) this.FindControl<Button>("UpdateButton")!.Content = "You're up to date.";
        }
        catch when (!reportCurrent) { }
        catch (Exception ex) { this.FindControl<Button>("UpdateButton")!.Content = $"Update check failed — {ex.Message}"; }
    }
    private async Task RunPeriodicUpdateChecksAsync(CancellationToken cancellationToken)
    {
        try
        {
            await CheckForUpdatesAsync(false);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
            while (await timer.WaitForNextTickAsync(cancellationToken)) await CheckForUpdatesAsync(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void UpdatePosLayout()
    {
        var grid = this.FindControl<Grid>("PosColumns");
        var products = this.FindControl<Border>("PosProducts");
        var cart = this.FindControl<Border>("PosCart");
        var summary = this.FindControl<Border>("PosSummary");
        if (grid is null || products is null || cart is null || summary is null) return;

        // Avalonia measures in device-independent pixels, so this also reacts to DPI scaling.
        var compact = Bounds.Width < (Classes.Contains("touch") ? 1250 : 1080);
        if (compact != _compactPos)
        {
            _compactPos = compact;
            grid.ColumnDefinitions = new ColumnDefinitions(compact ? "*" : "2*,3*,2*");
            grid.RowDefinitions = new RowDefinitions(compact ? "Auto,Auto,Auto" : "*");
            Grid.SetColumn(products, 0);
            Grid.SetColumn(cart, compact ? 0 : 1);
            Grid.SetColumn(summary, compact ? 0 : 2);
            Grid.SetRow(products, 0);
            Grid.SetRow(cart, compact ? 1 : 0);
            Grid.SetRow(summary, compact ? 2 : 0);
        }

        grid.Height = compact ? double.NaN : Math.Max(480, Bounds.Height - 185);
        products.Height = compact ? 340 : double.NaN;
        cart.Height = compact ? 340 : double.NaN;
        summary.Height = compact ? 500 : double.NaN;
        products.Margin = compact ? new Avalonia.Thickness(0, 0, 0, 10) : default;
        cart.Margin = compact ? new Avalonia.Thickness(0, 0, 0, 10) : default;
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        // Keep caret movement and multi-line notes native. Enter/vertical arrows
        // advance form fields without submitting a sale or changing a document.
        if (e.KeyModifiers != KeyModifiers.None || e.Source is not TextBox field
            || (field.AcceptsReturn && e.Key == Key.Enter)) return;
        var direction = e.Key switch
        {
            Key.Enter or Key.Down => NavigationDirection.Next,
            Key.Up => NavigationDirection.Previous,
            _ => (NavigationDirection?)null
        };
        if (direction is null) return;
        e.Handled = FocusManager?.TryMoveFocus(direction.Value) == true;
    }
}
