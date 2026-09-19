using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.EntityFrameworkCore;
using SugarERP.Kitchen;
using SugarERP.Infrastructure.Local;
using Avalonia.Platform.Storage;
using SugarERP.Application;
using System.Globalization;
using System.Runtime.Versioning;
using SugarERP.Desktop.Shared;

namespace SugarERP.Kitchen.App;
public sealed partial class MainWindow : Window
{
    private readonly KitchenStore _store; private readonly KitchenSyncService _sync; private KitchenRequestRecord? _selected;
    private bool _busy;
    private readonly CancellationTokenSource _backgroundStop = new();
    private readonly HttpClient _updateHttp = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly DeploymentConfiguration _deployment = DeploymentConfiguration.Create(DesktopApplicationType.Kitchen);
    private DesktopReleaseManifest? _availableUpdate;
    private readonly ReportDirectorySettings _reports = new("Kitchen");
    private Button SyncButton => this.FindControl<Button>("SyncButton")!;
    private TextBlock StatusText => this.FindControl<TextBlock>("StatusText")!;
    private ListBox RequestsList => this.FindControl<ListBox>("RequestsList")!;
    private TextBlock SelectedTitle => this.FindControl<TextBlock>("SelectedTitle")!;
    private DataGrid LinesGrid => this.FindControl<DataGrid>("LinesGrid")!;
    private TextBox ApiUrl => this.FindControl<TextBox>("ApiUrl")!;
    private TextBox EnrollmentToken => this.FindControl<TextBox>("EnrollmentToken")!;
    private TextBox DeviceName => this.FindControl<TextBox>("DeviceName")!;
    public MainWindow() { InitializeComponent(); _store = null!; _sync = null!; }
    public MainWindow(KitchenStore store, KitchenSyncService sync) { InitializeComponent(); _store = store; _sync = sync; DeviceName.Text = Environment.MachineName; ApiUrl.Text = _deployment.ApiBaseUrl.ToString().TrimEnd('/'); this.FindControl<TextBox>("ReportsDirectory")!.Text = _reports.GetDirectory(); this.FindControl<ComboBox>("PrinterName")!.ItemsSource = new WindowsRasterBranchPrinter().GetInstalledPrinterNames(); Opened += async (_, _) => { await RefreshAsync(); _ = RunBackgroundSyncAsync(_backgroundStop.Token); _ = RunPeriodicUpdateChecksAsync(_backgroundStop.Token); }; Closed += (_, _) => { _backgroundStop.Cancel(); _updateHttp.Dispose(); }; }
    private async Task RefreshAsync()
    {
        await using var db = _store.Open();
        RequestsList.ItemsSource = (await db.Requests.AsNoTracking().Include(x => x.Lines).ToListAsync()).OrderByDescending(x => x.SubmittedAtUtc).ToArray();
        this.FindControl<DataGrid>("ReceiptsGrid")!.ItemsSource = (await db.Receipts.AsNoTracking().ToListAsync()).OrderByDescending(x => x.CountedAtUtc).ToArray();
        this.FindControl<DataGrid>("ReturnsGrid")!.ItemsSource = (await db.Returns.AsNoTracking().ToListAsync()).OrderByDescending(x => x.DispatchedAtUtc).ToArray();
        this.FindControl<DataGrid>("InventoryGrid")!.ItemsSource = (await db.Ingredients.AsNoTracking().OrderBy(x => x.Name).ToListAsync()).Select(x => new IngredientEntry(x)).ToList();
        this.FindControl<ComboBox>("CafeCustomer")!.ItemsSource = (await db.CafeCustomers.AsNoTracking().Where(x => x.Active).OrderBy(x => x.Name).ToListAsync()).Select(x => new CafeCustomerOption(x.Id, x.Name)).ToArray();
        this.FindControl<DataGrid>("CustomOrdersGrid")!.ItemsSource = (await _sync.GetCustomOrdersAsync()).Select(x => new KitchenOrderRow(x)).ToArray();
        var configuration = await db.Configuration.AsNoTracking().SingleOrDefaultAsync();
        if (configuration is not null && !string.IsNullOrWhiteSpace(configuration.PrinterName)) this.FindControl<ComboBox>("PrinterName")!.SelectedItem = configuration.PrinterName;
    }
    private async void Enroll_Click(object? sender, RoutedEventArgs e) { await Run(async () => { await _sync.EnrollAsync(new Uri(ApiUrl.Text!.Trim()), EnrollmentToken.Text!.Trim(), DeviceName.Text!.Trim()); EnrollmentToken.Text = ""; return "تم ربط جهاز المطبخ."; }); }
    private async void Sync_Click(object? sender, RoutedEventArgs e) { await Run(async () => $"اكتملت المزامنة؛ تم تطبيق {await _sync.PullAsync()} تحديث."); await RefreshAsync(); }
    private async void Update_Click(object? sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null) { await CheckForUpdatesAsync(true); return; }
        await Run(async () =>
        {
            await _sync.PullAsync();
            await using var db = _store.Open();
            if (await db.Outbox.AnyAsync(x => !x.Acknowledged)) throw new InvalidOperationException("التحديث ينتظر إرسال كل عمليات المطبخ المحفوظة.");
            StatusText.Text = "Downloading update...";
            var path = await new DesktopUpdateService(_updateHttp, _deployment).DownloadAndVerifyAsync(_availableUpdate,
                new Progress<double>(x => StatusText.Text = $"Downloading update — {x:P0}"));
            await _store.CreateUpdateBackupAsync();
            DesktopUpdateService.LaunchInstaller(path); Close();
            return "Installing update...";
        });
    }
    private async Task CheckForUpdatesAsync(bool reportCurrent)
    {
        try
        {
            var result = await new DesktopUpdateService(_updateHttp, _deployment).CheckAsync(_backgroundStop.Token);
            _availableUpdate = result.UpdateAvailable ? result.Release : null;
            this.FindControl<Button>("UpdateButton")!.Content = result.UpdateAvailable ? result.Message : "Check for Updates";
            if (reportCurrent || result.UpdateAvailable) StatusText.Text = result.Message;
        }
        catch when (!reportCurrent) { }
        catch (Exception ex) { StatusText.Text = $"Update check failed — {ex.Message}"; }
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
    private void Request_Selected(object? sender, SelectionChangedEventArgs e)
    {
        _selected = RequestsList.SelectedItem as KitchenRequestRecord; if (_selected is null) return;
        SelectedTitle.Text = $"تجهيز طلب {_selected.BranchName}";
        LinesGrid.ItemsSource = _selected.Lines.Select(x => new DispatchLine(x)).ToList();
    }
    private async void Dispatch_Click(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_selected is null) { StatusText.Text = "اختر طلباً أولاً."; return; }
        var rows = (LinesGrid.ItemsSource as IEnumerable<DispatchLine>)!.ToArray();
        var confirm = new Window { Title = "تأكيد إرسال الشحنة", Width = 480, Height = 220, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var send = new Button { Content = "تأكيد الإرسال", IsDefault = true };
        var cancel = new Button { Content = "إلغاء", IsCancel = true };
        send.Click += (_, _) => confirm.Close(true); cancel.Click += (_, _) => confirm.Close(false);
        confirm.Content = new StackPanel { Margin = new Avalonia.Thickness(18), Spacing = 14, Children = { new TextBlock { Text = $"تأكيد إرسال الكميات المحددة إلى {_selected.BranchName}؟", TextWrapping = Avalonia.Media.TextWrapping.Wrap }, send, cancel } };
        if (!await confirm.ShowDialog<bool>(this)) return;
        await Run(async () => { await _sync.DispatchAsync(_selected.Id, rows.ToDictionary(x => x.Id, x => x.SendScaled)); return "تم حفظ الشحنة على الخادم ولن يتكرر الإرسال عند إعادة المحاولة."; });
        await RefreshAsync();
    }
    private async Task Run(Func<Task<string>> action) { if (_busy) return; _busy = true; try { SyncButton.IsEnabled = false; StatusText.Text = "جارٍ تنفيذ العملية..."; StatusText.Text = await action(); } catch (Exception ex) { StatusText.Text = ex.Message; } finally { _busy = false; SyncButton.IsEnabled = true; } }
    private async Task RunBackgroundSyncAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        do
        {
            if (!_busy)
            {
                try { cancellationToken.ThrowIfCancellationRequested(); var applied = await _sync.PullAsync(); await RefreshAsync(); StatusText.Text = applied == 0 ? "Synced" : $"Synced — applied {applied} updates"; }
                catch (InvalidOperationException ex) when (ex.Message.Contains("اربط", StringComparison.Ordinal)) { }
                catch (Exception) { StatusText.Text = "Offline — saved changes will retry automatically"; }
            }
        } while (await timer.WaitForNextTickAsync(cancellationToken));
    }
    private async void ReportsFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Excel & Reports Location", AllowMultiple = false });
        if (folders.Count == 0) return;
        await Run(() => { var path = folders[0].TryGetLocalPath() ?? throw new IOException("اختر مجلداً محلياً."); _reports.SaveDirectory(path); this.FindControl<TextBox>("ReportsDirectory")!.Text = path; return Task.FromResult("تم حفظ مجلد التقارير؛ الملفات السابقة لم تتغير."); });
    }
    private IReadOnlyDictionary<Guid, long> ReadIngredientInputs(bool requireAll) { var rows = (this.FindControl<DataGrid>("InventoryGrid")!.ItemsSource as IEnumerable<IngredientEntry>)?.ToArray() ?? []; return rows.Where(x => requireAll || !string.IsNullOrWhiteSpace(x.InputDisplay)).ToDictionary(x => x.ItemId, x => x.InputScaled); }
    private async void ReceiveIngredients_Click(object? sender, RoutedEventArgs e) { var reason = this.FindControl<TextBox>("InventoryReason")!.Text ?? ""; await Run(async () => { await _sync.ReceiveIngredientsAsync(ReadIngredientInputs(false), reason); return "تم تسجيل استلام الخامات ومزامنته بشكل موثق."; }); await RefreshAsync(); }
    private async void WasteIngredients_Click(object? sender, RoutedEventArgs e) { var reason = this.FindControl<TextBox>("InventoryReason")!.Text ?? ""; await Run(async () => { await _sync.RecordWasteAsync(ReadIngredientInputs(false), reason); return "تم تسجيل الهالك وخصمه مرة واحدة."; }); await RefreshAsync(); }
    private async void CountIngredients_Click(object? sender, RoutedEventArgs e) { if (!await ConfirmAsync("إقفال جرد اليوم", "سيتم حفظ الكميات الفعلية وفروق الجرد كسجل غير قابل للتعديل. هل تريد المتابعة؟")) return; await Run(async () => { await _sync.CountIngredientsAsync(ReadIngredientInputs(true)); var report = await new KitchenDailyReportWriter(_store).WriteLatestCountAsync(_reports.GetDirectory()); return $"تم إقفال الجرد وحفظ تقرير Excel غير قابل للاستبدال: {report.Path}"; }); await RefreshAsync(); }
    private async void CafeCustomer_Selected(object? sender, SelectionChangedEventArgs e)
    {
        if (this.FindControl<ComboBox>("CafeCustomer")!.SelectedItem is not CafeCustomerOption customer) return;
        await using var db = _store.Open();
        var prices = await db.CafePrices.AsNoTracking().Include(x => x.Item).Where(x => x.CustomerId == customer.Id && x.Item.Active).OrderBy(x => x.Item.Name).ToListAsync();
        this.FindControl<DataGrid>("CafeItemsGrid")!.ItemsSource = prices.Select(x => new CafeItemEntry(x)).ToList();
        this.FindControl<TextBox>("CustomDue")!.Text = DateTime.Now.AddDays(1).ToString("yyyy-MM-dd 12:00", CultureInfo.InvariantCulture);
    }
    private async void CreateCustomOrder_Click(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<ComboBox>("CafeCustomer")!.SelectedItem is not CafeCustomerOption customer) { StatusText.Text = "اختر العميل أولاً."; return; }
        if (!DateTime.TryParseExact(this.FindControl<TextBox>("CustomDue")!.Text?.Trim(), "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dueLocal)) { StatusText.Text = "اكتب موعد التسليم بهذا الشكل: 2026-09-20 18:30"; return; }
        var rows = ((IEnumerable<CafeItemEntry>?)this.FindControl<DataGrid>("CafeItemsGrid")!.ItemsSource ?? []).ToArray();
        IReadOnlyDictionary<Guid, long> quantities;
        try { quantities = rows.Where(x => x.QuantityScaled > 0).ToDictionary(x => x.ItemId, x => x.QuantityScaled); }
        catch (Exception ex) { StatusText.Text = ex.Message; return; }
        CustomOrderSnapshot? created = null;
        await Run(async () => { created = await _sync.CreateCustomOrderAsync(customer.Id, new DateTimeOffset(dueLocal).ToUniversalTime(), this.FindControl<TextBox>("CustomDescription")!.Text ?? "", quantities); return $"تم حفظ الفاتورة {created.OrderNumber} وإرسالها للخادم."; });
        await RefreshAsync();
        if (created is not null) await PrintOrderAsync(created.Id);
    }
    private async void PrintCustomOrder_Click(object? sender, RoutedEventArgs e) { if (this.FindControl<DataGrid>("CustomOrdersGrid")!.SelectedItem is not KitchenOrderRow row) { StatusText.Text = "اختر فاتورة للطباعة."; return; } await PrintOrderAsync(row.Id); }
    private async Task PrintOrderAsync(Guid orderId)
    {
        var printer = this.FindControl<ComboBox>("PrinterName")!.SelectedItem?.ToString() ?? "";
        await Run(async () => { await _sync.PrintCustomOrderAsync(orderId, printer); return "تم إرسال الفاتورة إلى الطابعة."; });
    }
    private async void PrinterTest_Click(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows()) { StatusText.Text = "الطباعة المباشرة متاحة على Windows فقط."; return; }
        var printerName = this.FindControl<ComboBox>("PrinterName")!.SelectedItem?.ToString() ?? "";
        await Run(async () => { await using var db = _store.Open(); var configuration = await db.Configuration.SingleAsync(); await PrintTestWindowsAsync(printerName, configuration.SiteName); configuration.PrinterName = printerName; await db.SaveChangesAsync(); return "تم إرسال صفحة اختبار للطابعة."; });
    }
#pragma warning disable CA1416
    private static Task PrintTestWindowsAsync(string printerName, string siteName) => new WindowsRasterBranchPrinter().PrintTestAsync(printerName, siteName);
#pragma warning restore CA1416
    private async Task<bool> ConfirmAsync(string title, string text) { var dialog = new Window { Title = title, Width = 500, Height = 230, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner }; var yes = new Button { Content = "تأكيد", IsDefault = true }; var no = new Button { Content = "إلغاء", IsCancel = true }; yes.Click += (_, _) => dialog.Close(true); no.Click += (_, _) => dialog.Close(false); dialog.Content = new StackPanel { Margin = new Avalonia.Thickness(18), Spacing = 14, Children = { new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, yes, no } }; return await dialog.ShowDialog<bool>(this); }
    private void InitializeComponent() => Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
}
public sealed class IngredientEntry
{
    private readonly KitchenIngredientBalance _item; public IngredientEntry(KitchenIngredientBalance item) { _item = item; }
    public Guid ItemId => _item.ItemId; public string Name => _item.Name; public string CurrentDisplay => $"{(decimal)_item.QuantityScaled / _item.QuantityScale:0.###} {_item.Unit}"; public string InputDisplay { get; set; } = "";
    public long InputScaled => decimal.TryParse(InputDisplay, out var value) && value >= 0 && decimal.Truncate(value * _item.QuantityScale) == value * _item.QuantityScale ? checked((long)(value * _item.QuantityScale)) : throw new InvalidOperationException($"كمية {_item.Name} غير صالحة.");
}
public sealed class DispatchLine
{
    private readonly KitchenRequestLineRecord _line;
    public DispatchLine(KitchenRequestLineRecord line) { _line = line; SendDisplay = ((decimal)(line.RequestedScaled - line.SentScaled) / line.QuantityScale).ToString("0.###"); }
    public Guid Id => _line.Id; public string Name => _line.Name; public string RequestedDisplay => $"{(decimal)_line.RequestedScaled / _line.QuantityScale:0.###} {_line.Unit}"; public string SentDisplay => $"{(decimal)_line.SentScaled / _line.QuantityScale:0.###} {_line.Unit}"; public string SendDisplay { get; set; }
    public long SendScaled => decimal.TryParse(SendDisplay, out var value) && value >= 0 && decimal.Truncate(value * _line.QuantityScale) == value * _line.QuantityScale ? checked((long)(value * _line.QuantityScale)) : throw new InvalidOperationException($"كمية {_line.Name} غير صالحة.");
}
public sealed record CafeCustomerOption(Guid Id, string Name) { public override string ToString() => Name; }
public sealed class CafeItemEntry
{
    private readonly KitchenCafePrice _price; public CafeItemEntry(KitchenCafePrice price) { _price = price; }
    public Guid ItemId => _price.ItemId; public string Name => _price.Item.Name; public string PriceDisplay => $"{_price.UnitPriceMinor / 100m:0.00} ج.م"; public string QuantityText { get; set; } = "0";
    public long QuantityScaled => decimal.TryParse(QuantityText, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value >= 0 && decimal.Truncate(value * _price.Item.QuantityScale) == value * _price.Item.QuantityScale ? checked((long)(value * _price.Item.QuantityScale)) : throw new InvalidOperationException($"كمية {Name} غير صالحة.");
}
public sealed class KitchenOrderRow
{
    public KitchenOrderRow(CustomOrderSnapshot order) { Id = order.Id; OrderNumber = order.OrderNumber; CustomerName = order.CustomerName; DueDisplay = order.DueAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"); TotalDisplay = $"{order.TotalMinor / 100m:0.00} ج.م"; }
    public Guid Id { get; } public string OrderNumber { get; } public string CustomerName { get; } public string DueDisplay { get; } public string TotalDisplay { get; }
}
