using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Microsoft.EntityFrameworkCore;
using SugarERP.Application;
using SugarERP.Domain;
using SugarERP.Infrastructure.Local;
using SugarERP.Desktop.Shared;

namespace SugarERP.Branch2.App;

public sealed partial class MainWindow : Window
{
    private readonly Branch2ApplicationService _service;
    private readonly ReportDirectorySettings _reports = new("Branch2");
    private readonly CancellationTokenSource _backgroundStop = new();
    private readonly HttpClient _updateHttp = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly DeploymentConfiguration _deployment = DeploymentConfiguration.Create(DesktopApplicationType.BranchType2);
    private DesktopReleaseManifest? _availableUpdate;
    private CatalogItemSnapshot? _selectedItem;
    private bool _busy;
    public MainWindow() { AvaloniaXamlLoader.Load(this); _service = null!; }
    public MainWindow(Branch2ApplicationService service)
    {
        AvaloniaXamlLoader.Load(this); _service = service;
        Control<TextBox>("ReportsDirectory").Text = _reports.GetDirectory();
        Control<TextBox>("ApiUrl").Text = DeploymentConfiguration.Create(DesktopApplicationType.BranchType2).ApiBaseUrl.ToString().TrimEnd('/');
        Opened += async (_, _) => { await Run(Refresh); _ = RunBackgroundSyncAsync(_backgroundStop.Token); _ = RunPeriodicUpdateChecksAsync(_backgroundStop.Token); };
        Closed += (_, _) => { _backgroundStop.Cancel(); _updateHttp.Dispose(); };
    }
    private T Control<T>(string name) where T : Avalonia.Controls.Control => this.FindControl<T>(name)!;
    private async Task Refresh()
    {
        var catalog = await new BranchModuleOperationsService(_service.Database).GetCatalogAsync();
        Control<DataGrid>("ItemsGrid").ItemsSource = catalog.Items.Select(x => new CatalogChoice(x)).ToArray();
        Control<DataGrid>("StockGrid").ItemsSource = await _service.StockAsync();
        Control<ComboBox>("Cafes").ItemsSource = (await new BranchModuleOperationsService(_service.Database).GetCafeProfilesAsync()).Select(x => new CafeChoice(x)).ToArray();
        Control<DataGrid>("CafeOrders").ItemsSource = await _service.CafeOrdersAsync();
        Control<ComboBox>("Shipments").ItemsSource = (await _service.ShipmentsAsync()).Where(x => x.IsReceivable).Select(x => new ShipmentChoice(x)).ToArray();
        await using var db = _service.Database.CreateContext();
        var history = await db.InventoryTransactions.AsNoTracking().Include(x => x.Lines).ToListAsync();
        Control<DataGrid>("History").ItemsSource = history.OrderByDescending(x => x.OccurredAtUtc).SelectMany(x => x.Lines.Select(line => new { x.OccurredAtUtc, x.Kind, x.ReferenceId, x.UserId, line.ItemId, line.Location, line.DeltaScaled, x.Reason })).ToArray();
    }
    private void NewItem_Click(object? sender, RoutedEventArgs e)
    {
        _selectedItem = null;
        Control<DataGrid>("ItemsGrid").SelectedItem = null;
        Control<TextBox>("ItemName").Text = "";
        Control<TextBox>("ItemUnit").Text = "قطعة";
        Control<TextBox>("ItemPrice").Text = "";
        Control<TextBlock>("Status").Text = "أدخل بيانات الصنف الجديد.";
    }
    private void Item_Selected(object? sender, SelectionChangedEventArgs e)
    {
        if (Control<DataGrid>("ItemsGrid").SelectedItem is not CatalogChoice choice) return;
        _selectedItem = choice.Item;
        Control<TextBox>("ItemName").Text = choice.Item.NameAr;
        Control<TextBox>("ItemUnit").Text = choice.Item.Unit;
        Control<TextBox>("ItemPrice").Text = (choice.Item.RetailPriceMinor / 100m).ToString("0.00");
    }
    private async void SaveItem_Click(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        var name = Control<TextBox>("ItemName").Text?.Trim() ?? "";
        var unit = Control<TextBox>("ItemUnit").Text?.Trim() ?? "";
        if (name.Length == 0 || unit.Length == 0) throw new BusinessRuleException("CATALOG_FIELDS_REQUIRED", "اكتب اسم الصنف ووحدته.");
        if (!decimal.TryParse(Control<TextBox>("ItemPrice").Text, out var price) || price < 0 || decimal.Truncate(price * 100) != price * 100)
            throw new BusinessRuleException("INVALID_PRICE", "أدخل سعر بيع صحيحاً بالجنيه.");
        var commandId = Guid.NewGuid();
        var saved = await new BranchModuleOperationsService(_service.Database).SaveCatalogItemAsync(new SaveCatalogItemCommand(
            commandId, _selectedItem?.Id, _selectedItem?.Version, _selectedItem?.Sku ?? $"AUTO-{commandId:N}", name, unit,
            _selectedItem?.QuantityScale ?? 1, checked((long)(price * 100))));
        _selectedItem = saved;
        await Refresh();
        Control<TextBlock>("Status").Text = "تم حفظ الصنف محلياً؛ سيظهر في لوحة الإدارة بعد المزامنة.";
    });
    private async void ArchiveItem_Click(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        var item = _selectedItem ?? throw new BusinessRuleException("ITEM_REQUIRED", "اختر صنفاً للأرشفة.");
        await new BranchModuleOperationsService(_service.Database).ArchiveCatalogItemAsync(Guid.NewGuid(), item.Id, item.Version);
        _selectedItem = null;
        await Refresh();
        Control<TextBlock>("Status").Text = "تمت أرشفة الصنف محلياً؛ ستُزامن الحالة مع لوحة الإدارة.";
    });
    private async Task Run(Func<Task> action)
    {
        if (_busy) return; _busy = true;
        try { Control<TextBlock>("Status").Text = "جارٍ تنفيذ العملية..."; await action(); }
        catch (Exception ex) { Control<TextBlock>("Status").Text = ex is BusinessRuleException ? ex.Message : "تعذر تنفيذ العملية. البيانات المحفوظة لم تحذف؛ تحقق من الاتصال وأعد المحاولة."; }
        finally { _busy = false; }
    }
    private async Task RunBackgroundSyncAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        do
        {
            if (!_busy)
            {
                try { cancellationToken.ThrowIfCancellationRequested(); var result = await _service.SyncAsync(); await Refresh(); Control<TextBlock>("Status").Text = result.UserMessage; }
                catch (BusinessRuleException ex) when (ex.Code == "NOT_ENROLLED") { }
                catch (Exception) { Control<TextBlock>("Status").Text = "Offline — saved changes will retry automatically"; }
            }
        } while (await timer.WaitForNextTickAsync(cancellationToken));
    }
    private (LocationStockRow Item, long Quantity) Selected()
    {
        var row = Control<DataGrid>("StockGrid").SelectedItem as LocationStockRow ?? throw new BusinessRuleException("ITEM_REQUIRED", "اختر صنفاً.");
        if (!decimal.TryParse(Control<TextBox>("Quantity").Text, out var quantity) || quantity <= 0 || decimal.Truncate(quantity * row.Scale) != quantity * row.Scale)
            throw new BusinessRuleException("INVALID_QUANTITY", "أدخل كمية صحيحة بوحدة الصنف.");
        return (row, checked((long)(quantity * row.Scale)));
    }
    private async void CafeCreate_Click(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        var cafe = Control<ComboBox>("Cafes").SelectedItem as CafeChoice ?? throw new BusinessRuleException("CUSTOMER_REQUIRED", "اختر العميل.");
        var (item, quantity) = Selected();
        var order = await _service.CreateCafeOrderAsync(new(Guid.NewGuid(), cafe.Profile.Id, Control<TextBox>("Reason").Text ?? "Cafe order", DateTimeOffset.UtcNow.AddDays(1), [new CafeOrderLineInput(item.ItemId, quantity)]));
        await Refresh(); Control<TextBlock>("Status").Text = $"تم حفظ الطلب {order.OrderNumber} محلياً؛ بانتظار المزامنة. لم يخصم المخزون حتى التسليم.";
    });
    private async void CafeArchive_Click(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        var cafe = Control<ComboBox>("Cafes").SelectedItem as CafeChoice ?? throw new BusinessRuleException("CUSTOMER_REQUIRED", "اختر الكافيه.");
        if (!await Confirm($"حذف {cafe.Profile.Name} من الحسابات الجديدة؟ ستظل الطلبات والمدفوعات السابقة محفوظة.")) return;
        await _service.ArchiveCafeAsync(Guid.NewGuid(), cafe.Profile.Id, cafe.Profile.Version);
        await Refresh(); Control<TextBlock>("Status").Text = "تم حذف الكافيه محلياً؛ ستصل الأرشفة إلى الخادم وباقي الأجهزة عبر المزامنة.";
    });
    private async void CafeDeliver_Click(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        var order = Control<DataGrid>("CafeOrders").SelectedItem as CustomOrderSnapshot ?? throw new BusinessRuleException("ORDER_REQUIRED", "اختر الطلب.");
        if (!await Confirm($"تسليم الطلب {order.OrderNumber} للعميل {order.CustomerName} وخصم أصنافه من Stock؟")) return;
        await _service.DeliverCafeOrderAsync(Guid.NewGuid(), order.Id, order.Version);
        await Refresh(); Control<TextBlock>("Status").Text = "تم تسجيل التسليم وخصم Stock محلياً؛ بانتظار المزامنة.";
    });
    private async Task<bool> Confirm(string message)
    {
        var dialog = new Window { Title = "تأكيد العملية", Width = 540, Height = 260, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, FlowDirection = Avalonia.Media.FlowDirection.RightToLeft };
        var accept = new Button { Content = "تأكيد", IsDefault = true };
        var cancel = new Button { Content = "إلغاء", IsCancel = true };
        accept.Click += (_, _) => dialog.Close(true); cancel.Click += (_, _) => dialog.Close(false);
        dialog.Content = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 18, Children = {
            new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, Children = { accept, cancel } }
        } };
        return await dialog.ShowDialog<bool>(this);
    }
    private async void Return_Click(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        var (item, quantity) = Selected();
        var reason = Control<TextBox>("Reason").Text?.Trim() ?? "";
        if (reason.Length < 3) throw new BusinessRuleException("REASON_REQUIRED", "اكتب سبب المرتجع.");
        if (!await Confirm($"إرسال مرتجع للمطبخ: {quantity / (decimal)item.Scale} {item.Unit} من {item.Name}؟ سيخصم من Stock. السبب: {reason}")) return;
        var result = await _service.ReturnToKitchenAsync(Guid.NewGuid(), reason, [new QuantityInput(item.ItemId, quantity)]);
        await Refresh(); Control<TextBlock>("Status").Text = $"تم حفظ المرتجع {result.Reference} محلياً؛ بانتظار المزامنة.";
    });
    private async void CafePayment_Click(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        var order = Control<DataGrid>("CafeOrders").SelectedItem as CustomOrderSnapshot ?? throw new BusinessRuleException("ORDER_REQUIRED", "اختر الطلب.");
        if (!decimal.TryParse(Control<TextBox>("CafePaymentAmount").Text, out var amount) || amount <= 0 || decimal.Truncate(amount * 100) != amount * 100)
            throw new BusinessRuleException("INVALID_PAYMENT", "أدخل مبلغاً صحيحاً بالجنيه وبحد أقصى رقمين عشريين.");
        await _service.CollectCafePaymentAsync(Guid.NewGuid(), order.Id, order.Version, checked((long)(amount * 100)), Control<ComboBox>("CafePaymentMethod").SelectedIndex == 1 ? PaymentMethod.Visa : PaymentMethod.Cash);
        await Refresh(); Control<TextBlock>("Status").Text = "تم حفظ الدفعة محلياً؛ بانتظار المزامنة. دفعات الكافيه منفصلة عن نقدية مبيعات الوردية.";
    });
    private async void Enroll_Click(object? s, RoutedEventArgs e) => await Run(async () => { await _service.EnrollAsync(new Uri(Control<TextBox>("ApiUrl").Text!), Control<TextBox>("Enrollment").Text!, Environment.MachineName); Control<TextBox>("Enrollment").Text = ""; await Refresh(); Control<TextBlock>("Status").Text = "تم ربط الجهاز وتحميل الكتالوج."; });
    private async void Login_Click(object? s, RoutedEventArgs e) => await Run(async () => { await _service.LoginAsync(Control<TextBox>("Username").Text!, Control<TextBox>("Password").Text!); Control<TextBox>("Password").Text = ""; Control<TextBlock>("Status").Text = "تم تسجيل الدخول بصلاحية تشغيل معتمدة من الخادم."; });
    private async void Sync_Click(object? s, RoutedEventArgs e) => await Run(async () => { var result = await _service.SyncAsync(); await Refresh(); Control<TextBlock>("Status").Text = result.UserMessage; });
    private async void Update_Click(object? sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null) { await CheckForUpdatesAsync(true); return; }
        await Run(async () =>
        {
            await _service.SyncAsync();
            await using var db = _service.Database.CreateContext();
            if (await db.Shifts.AnyAsync(x => x.Status == ShiftStatus.Open)) throw new BusinessRuleException("UPDATE_BLOCKED", "أغلق الوردية قبل التحديث.");
            if (await db.OutboxMessages.AnyAsync(x => x.State != OutboxState.Acknowledged)) throw new BusinessRuleException("UPDATE_BLOCKED", "التحديث ينتظر إرسال كل العمليات المحفوظة.");
            Control<TextBlock>("Status").Text = "Downloading update...";
            var updater = new DesktopUpdateService(_updateHttp, _deployment);
            var path = await updater.DownloadAndVerifyAsync(_availableUpdate, new Progress<double>(x => Control<TextBlock>("Status").Text = $"Downloading update — {x:P0}"));
            await _service.Database.CreateUpdateBackupAsync();
            DesktopUpdateService.LaunchInstaller(path);
            Close();
        });
    }
    private async Task CheckForUpdatesAsync(bool reportCurrent)
    {
        try
        {
            var result = await new DesktopUpdateService(_updateHttp, _deployment).CheckAsync(_backgroundStop.Token);
            _availableUpdate = result.UpdateAvailable ? result.Release : null;
            Control<Button>("UpdateButton").Content = result.UpdateAvailable ? result.Message : "Check for Updates";
            if (reportCurrent || result.UpdateAvailable) Control<TextBlock>("Status").Text = result.Message;
        }
        catch when (!reportCurrent) { }
        catch (Exception ex) { Control<TextBlock>("Status").Text = $"Update check failed — {ex.Message}"; }
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
    private async void Move_Click(object? s, RoutedEventArgs e) => await Run(async () => { var (item, quantity) = Selected(); await _service.MoveAsync(Guid.NewGuid(), Guid.NewGuid(), new Dictionary<Guid, long> { [item.ItemId] = quantity }, false, Control<TextBox>("Reason").Text ?? ""); await Refresh(); Control<TextBlock>("Status").Text = "تم حفظ النقل محلياً؛ بانتظار المزامنة مع الخادم."; });
    private async void EndDay_Click(object? s, RoutedEventArgs e) => await Run(async () =>
    {
        var preview = await _service.ClosingPreviewAsync();
        var rows = preview.Items.Select(x => new ClosingRow(x)).ToArray();
        var cash = new TextBox { Text = (preview.ExpectedCashMinor / 100m).ToString("0.00"), PlaceholderText = "النقدية الفعلية EGP" };
        var grid = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, Height = 370 };
        grid.Columns.Add(new DataGridTextColumn { Header = "الصنف", Binding = new Avalonia.Data.Binding(nameof(ClosingRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });
        grid.Columns.Add(new DataGridTextColumn { Header = "المتوقع", Binding = new Avalonia.Data.Binding(nameof(ClosingRow.Expected)), Width = new DataGridLength(130), IsReadOnly = true });
        grid.Columns.Add(new DataGridTextColumn { Header = "العد الفعلي", Binding = new Avalonia.Data.Binding(nameof(ClosingRow.Actual)), Width = new DataGridLength(150) });
        var close = new Button { Content = "إرجاع Display وإغلاق الوردية", IsDefault = true }; var cancel = new Button { Content = "إلغاء", IsCancel = true };
        var dialog = new Window { Title = "عد وإغلاق اليوم", Width = 760, Height = 590, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Avalonia.Thickness(18), Spacing = 10, Children = { new TextBlock { Text = "عد كل المخزون الفعلي (Stock + Display). سيعود Display إلى Stock وتغلق الوردية في معاملة واحدة.", TextWrapping = Avalonia.Media.TextWrapping.Wrap }, grid, cash,
                new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { close, cancel } } } } };
        close.Click += (_, _) => dialog.Close(true); cancel.Click += (_, _) => dialog.Close(false);
        if (!await dialog.ShowDialog<bool>(this)) return;
        if (!decimal.TryParse(cash.Text, out var actualCash) || actualCash < 0 || decimal.Truncate(actualCash * 100) != actualCash * 100) throw new BusinessRuleException("INVALID_CASH", "أدخل النقدية الفعلية بدقة صحيحة.");
        var result = await _service.CloseDayAsync(Guid.NewGuid(), rows.Select(x => x.ToInput()).ToArray(), checked((long)(actualCash * 100)));
        await Refresh(); Control<TextBlock>("Status").Text = result.ReportError ?? $"أغلقت الوردية {result.Close.ShiftId}. تم إنشاء تقرير Excel: {result.ReportPath} وحفظ حركة Display للمزامنة.";
    });
    private async void OpenShift_Click(object? s, RoutedEventArgs e) => await Run(async () => { await _service.OpenShiftAsync(ShiftKind.Morning, 0); Control<TextBlock>("Status").Text = "تم فتح الوردية."; });
    private async void Sell_Click(object? s, RoutedEventArgs e) => await Run(async () => { var (item, quantity) = Selected(); var receipt = await _service.SellAsync(Guid.NewGuid(), [new SaleCartLine(item.ItemId, quantity)], PaymentMethod.Cash, 0, 0); await Refresh(); Control<TextBlock>("Status").Text = $"تم حفظ البيع {receipt.ReceiptNumber} · {receipt.TotalMinor / 100m:0.00} EGP. بانتظار المزامنة والطباعة."; });
    private async void Request_Click(object? s, RoutedEventArgs e) => await Run(async () => { var (item, quantity) = Selected(); await _service.RequestAsync(Guid.NewGuid(), [new QuantityInput(item.ItemId, quantity)]); Control<TextBlock>("Status").Text = "تم حفظ طلب الوارد؛ اضغط مزامنة لإرساله إلى الخادم."; });
    private void Shipment_Selected(object? s, SelectionChangedEventArgs e) { if (Control<ComboBox>("Shipments").SelectedItem is ShipmentChoice choice) Control<DataGrid>("ReceiptGrid").ItemsSource = choice.Shipment.Lines.Select(x => new ReceiptRow(x)).ToArray(); }
    private async void Receive_Click(object? s, RoutedEventArgs e) => await Run(async () => { var choice = Control<ComboBox>("Shipments").SelectedItem as ShipmentChoice ?? throw new BusinessRuleException("SHIPMENT_REQUIRED", "اختر شحنة."); var rows = (IEnumerable<ReceiptRow>)Control<DataGrid>("ReceiptGrid").ItemsSource!; var result = await _service.ReceiveAsync(new ReceiveShipmentCommand(Guid.NewGuid(), choice.Shipment.Id, choice.Shipment.Version, rows.Select(x => x.Input()).ToArray())); await Refresh(); Control<TextBlock>("Status").Text = result.EntireShipmentHeld ? "الشحنة محتجزة لحين قرار الإدارة؛ لم يزد المخزون." : "تم قبول الوارد إلى Stock فقط؛ بانتظار المزامنة."; });
    private async void ManualIncoming_Click(object? s, RoutedEventArgs e) => await Run(async () => { var (item, quantity) = Selected(); var reason = Control<TextBox>("Reason").Text?.Trim() ?? ""; await _service.PostManualIncomingAsync(Guid.NewGuid(), reason, [new QuantityInput(item.ItemId, quantity)]); var sync = await _service.SyncAsync(); await Refresh(); Control<TextBlock>("Status").Text = sync.Succeeded ? "تمت إضافة الوارد اليدوي إلى Stock ومزامنته مع الخادم." : $"تم حفظ الوارد اليدوي محلياً. {sync.UserMessage}"; });
    private async void Reports_Click(object? s, RoutedEventArgs e) => await Run(async () => { var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Excel / Reports Location" }); if (folders.Count == 0) return; _reports.SaveDirectory(folders[0].TryGetLocalPath() ?? throw new IOException()); Control<TextBox>("ReportsDirectory").Text = _reports.GetDirectory(); Control<TextBlock>("Status").Text = "تم حفظ مجلد التقارير."; });
}
public sealed record ShipmentChoice(IncomingShipmentSnapshot Shipment) { public override string ToString() => Shipment.Reference; }
public sealed record CafeChoice(CafeProfileSnapshot Profile) { public override string ToString() => Profile.Name; }
public sealed record CatalogChoice(CatalogItemSnapshot Item)
{
    public string NameAr => Item.NameAr;
    public string Unit => Item.Unit;
    public string Price => $"{Item.RetailPriceMinor / 100m:0.00} EGP";
    public string State => Item.Active ? "نشط" : "مؤرشف";
}
public sealed class ReceiptRow(ShipmentLineSnapshot line)
{
    public string Name => line.ItemName;
    public decimal Sent => (decimal)line.SentScaled / line.QuantityScale;
    public string Counted { get; set; } = "";
    public ShipmentCountInput Input() { if (!decimal.TryParse(Counted, out var value) || value < 0 || decimal.Truncate(value * line.QuantityScale) != value * line.QuantityScale) throw new BusinessRuleException("COUNT_REQUIRED", "أدخل العد الفعلي لكل صنف، بما فيه الصفر."); return new(line.Id, checked((long)(value * line.QuantityScale))); }
}
public sealed class ClosingRow(ClosingItemSnapshot item)
{
    public string Name => item.ItemName; public string Expected => ((decimal)item.ExpectedScaled / item.QuantityScale).ToString("0.###"); public string Actual { get; set; } = ((decimal)item.ExpectedScaled / item.QuantityScale).ToString("0.###");
    public ClosingCountInput ToInput() { if (!decimal.TryParse(Actual, out var value) || value < 0 || decimal.Truncate(value * item.QuantityScale) != value * item.QuantityScale) throw new BusinessRuleException("INVALID_COUNT", $"راجع عد {item.ItemName}."); return new(item.ItemId, checked((long)(value * item.QuantityScale))); }
}
