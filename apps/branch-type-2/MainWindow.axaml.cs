using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SugarERP.Desktop.Shared.ViewModels;

namespace SugarERP.Branch2;

public sealed partial class MainWindow : Window
{
    private readonly BranchPosViewModel _viewModel;
    private bool _compactPos;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = null!;
        SizeChanged += (_, _) => UpdatePosLayout();
    }

    public MainWindow(BranchPosViewModel viewModel, bool isTouch = false)
    {
        InitializeComponent();
        _viewModel = viewModel;
        if (isTouch) Classes.Add("touch");
        DataContext = viewModel;
        Opened += OnOpened;
        SizeChanged += (_, _) => UpdatePosLayout();
        AddHandler(KeyDownEvent, OnFormKeyDown, RoutingStrategies.Tunnel);
    }

    private async void OnOpened(object? sender, EventArgs eventArgs)
    {
        Opened -= OnOpened;
        await _viewModel.InitializeAsync();
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
