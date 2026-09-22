using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.EntityFrameworkCore;
using SugarERP.Kitchen;
using SugarERP.Infrastructure.Local;
using Avalonia.Platform.Storage;
using SugarERP.Application;
using SugarERP.Domain;
using SugarERP.Sync.Client;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using Avalonia.Media;
using SugarERP.Desktop.Shared;

namespace SugarERP.Kitchen.App;
public sealed partial class MainWindow : Window
{
    private readonly KitchenStore _store; private readonly KitchenSyncService _sync; private KitchenRequestRecord? _selected;
    private KitchenProduct? _selectedItem;
    private Guid? _selectedCafeId;
    private int _selectedCafeVersion;
    private readonly Dictionary<Guid, long> _recipeDraft = [];
    private int _recipeExpectedVersion;
    private bool _loadingRecipeChoices;
    private WaredRow[] _waredRows = [];
    private string _waredFilter = "PENDING";
    private bool _busy;
    private readonly CancellationTokenSource _backgroundStop = new();
    private readonly HttpClient _updateHttp = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly DeploymentConfiguration _deployment = DeploymentConfiguration.Create(DesktopApplicationType.Kitchen);
    private DesktopReleaseManifest? _availableUpdate;
    private RecipeRow[] _recipeRows = [];
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
    public MainWindow(KitchenStore store, KitchenSyncService sync) { InitializeComponent(); _store = store; _sync = sync; DeviceName.Text = Environment.MachineName; ApiUrl.Text = _deployment.ApiBaseUrl.ToString().TrimEnd('/'); this.FindControl<TextBox>("ReportsDirectory")!.Text = _reports.GetDirectory(); this.FindControl<ComboBox>("PrinterName")!.ItemsSource = new WindowsRasterBranchPrinter().GetInstalledPrinterNames(); Opened += async (_, _) => { SetConnectionState("SYNCING"); await RefreshAsync(); _ = RunBackgroundSyncAsync(_backgroundStop.Token); _ = RunPeriodicUpdateChecksAsync(_backgroundStop.Token); }; Closed += (_, _) => { _backgroundStop.Cancel(); _updateHttp.Dispose(); }; }
    private async Task RefreshAsync()
    {
        await using var db = _store.Open();
        var requests = (await db.Requests.AsNoTracking().Include(x => x.Lines).ToListAsync()).OrderByDescending(x => x.SubmittedAtUtc).ToArray();
        var activeRequests = requests.Where(x => x.Status is not ("FULFILLED" or "SENT" or "REJECTED")).ToArray();
        var shipmentOutbox = (await db.Outbox.AsNoTracking().Where(x => x.UploadJson.Contains("shipment.dispatched")).OrderByDescending(x => x.Sequence).ToListAsync())
            .Select(ParseUpload).Where(x => x is not null).Cast<SyncUploadEvent>().ToArray();
        var receipts = await _sync.GetReceiptsAsync();
        var selectedRequestId = (RequestsList.SelectedItem as WaredRow)?.Request.Id;
        _waredRows = requests.Select(request => WaredRow.Create(request,
            shipmentOutbox.FirstOrDefault(x => x.Payload.TryGetProperty("request_id", out var requestId) && requestId.GetGuid() == request.Id), receipts)).ToArray();
        ApplyWaredFilter(selectedRequestId);
        this.FindControl<DataGrid>("ReturnsGrid")!.ItemsSource = (await db.Returns.AsNoTracking().ToListAsync()).OrderByDescending(x => x.DispatchedAtUtc).ToArray();
        var ingredients = await db.Ingredients.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        var ingredientsById = ingredients.ToDictionary(x => x.ItemId);
        var recipes = await db.Recipes.AsNoTracking().Include(x => x.Components).ToListAsync();
        var recipesByProduct = recipes.ToDictionary(x => x.ProductItemId);
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id);
        var selectedItemId = _selectedItem?.Id;
        var itemRows = products.Values.Where(x => x.Active).OrderBy(x => x.Name)
            .Select(x => new KitchenItemRow(x, ingredientsById.GetValueOrDefault(x.Id), recipesByProduct.GetValueOrDefault(x.Id), ingredientsById)).ToArray();
        this.FindControl<DataGrid>("ProductsGrid")!.ItemsSource = itemRows.Where(x => x.Item.Kind == "PRODUCT").ToArray();
        this.FindControl<DataGrid>("IngredientsGrid")!.ItemsSource = itemRows.Where(x => x.Item.Kind == "INGREDIENT").ToArray();
        if (selectedItemId is Guid selectedId && products.TryGetValue(selectedId, out var selectedProduct))
            (selectedProduct.Kind == "INGREDIENT" ? this.FindControl<DataGrid>("IngredientsGrid")! : this.FindControl<DataGrid>("ProductsGrid")!)
                .SelectedItem = itemRows.FirstOrDefault(x => x.Item.Id == selectedId);
        var selectedRecipeProductId = (this.FindControl<ComboBox>("RecipeProduct")!.SelectedItem as ProductChoice)?.Id;
        _loadingRecipeChoices = true;
        this.FindControl<ComboBox>("RecipeProduct")!.ItemsSource = products.Values.Where(x => x.Active && x.Kind == "PRODUCT")
            .OrderBy(x => x.Name).Select(x => new ProductChoice(x.Id, x.Name)).ToArray();
        this.FindControl<ComboBox>("RecipeProduct")!.SelectedItem = ((IEnumerable<ProductChoice>)this.FindControl<ComboBox>("RecipeProduct")!.ItemsSource!)
            .FirstOrDefault(x => x.Id == selectedRecipeProductId);
        var ingredientChoices = products.Values.Where(x => x.Active && x.Kind == "INGREDIENT")
            .OrderBy(x => x.Name).Select(x => new ProductChoice(x.Id, x.Name)).ToArray();
        this.FindControl<ComboBox>("RecipeIngredient")!.ItemsSource = ingredientChoices;
        this.FindControl<ComboBox>("PurchaseIngredient")!.ItemsSource = ingredientChoices;
        _loadingRecipeChoices = false;
        RefreshRecipeDraft(products, ingredientsById);
        var committed = new Dictionary<Guid, long>();
        foreach (var line in activeRequests.SelectMany(x => x.Lines))
        {
            var remaining = Math.Max(0, line.RequestedScaled - line.SentScaled);
            if (remaining == 0 || !recipesByProduct.TryGetValue(line.ItemId, out var recipe) || recipe.OutputScaled <= 0) continue;
            foreach (var component in recipe.Components)
            {
                var required = checked((long)decimal.Ceiling((decimal)remaining * component.QuantityScaled / recipe.OutputScaled));
                committed[component.IngredientItemId] = checked(committed.GetValueOrDefault(component.IngredientItemId) + required);
            }
        }
        var inventoryRows = ingredients.Select(x => new IngredientEntry(x, checked(x.QuantityScaled - committed.GetValueOrDefault(x.ItemId)))).ToList();
        this.FindControl<DataGrid>("InventoryGrid")!.ItemsSource = inventoryRows;
        _recipeRows = recipes.OrderBy(x => products.GetValueOrDefault(x.ProductItemId)?.Name ?? x.ProductItemId.ToString()).Select(recipe =>
        {
            var product = products.GetValueOrDefault(recipe.ProductItemId);
            var productScale = product?.QuantityScale is > 0 ? product.QuantityScale : 1;
            var components = recipe.Components.Select(component =>
            {
                var ingredient = ingredientsById.GetValueOrDefault(component.IngredientItemId);
                var scale = ingredient?.QuantityScale is > 0 ? ingredient.QuantityScale : 1;
                return $"{ingredient?.Name ?? component.IngredientItemId.ToString()} {(decimal)component.QuantityScaled / scale:0.###} {ingredient?.Unit ?? ""}".Trim();
            });
            return new RecipeRow
            {
                ProductName = product?.Name ?? recipe.ProductItemId.ToString(),
                OutputDisplay = $"{(decimal)recipe.OutputScaled / productScale:0.###} {product?.Unit ?? ""}".Trim(),
                ComponentsDisplay = string.Join("، ", components),
                Version = recipe.Version
            };
        }).ToArray();
        this.FindControl<TextBlock>("RecipeCost")!.Text = _recipeRows.Length == 0 ? "تكلفة الوصفة: —" : this.FindControl<TextBlock>("RecipeCost")!.Text;
        var cafeOptions = (await db.CafeCustomers.AsNoTracking().Where(x => x.Active && !x.HiddenLocally).OrderBy(x => x.Name).ToListAsync())
            .Select(x => new CafeCustomerOption(x.Id, x.Name)).ToArray();
        this.FindControl<ComboBox>("CafeCustomer")!.ItemsSource = cafeOptions;
        this.FindControl<ComboBox>("CafeCustomer")!.SelectedItem = cafeOptions.FirstOrDefault(x => x.Id == _selectedCafeId);
        var customOrders = await _sync.GetCustomOrdersAsync();
        this.FindControl<DataGrid>("CustomOrdersGrid")!.ItemsSource = customOrders.Select(x => new KitchenOrderRow(x)).ToArray();
        var configuration = await db.Configuration.AsNoTracking().SingleOrDefaultAsync();
        this.FindControl<StackPanel>("EnrollmentPanel")!.IsVisible = configuration is null;
        if (configuration is not null && !string.IsNullOrWhiteSpace(configuration.PrinterName)) this.FindControl<ComboBox>("PrinterName")!.SelectedItem = configuration.PrinterName;
        var pendingSync = await db.Outbox.AsNoTracking().CountAsync(x => !x.Acknowledged);
        this.FindControl<TextBlock>("PendingSyncCount")!.Text = pendingSync.ToString(CultureInfo.CurrentCulture);
        this.FindControl<TextBlock>("IngredientCount")!.Text = ingredients.Count.ToString(CultureInfo.CurrentCulture);
        this.FindControl<TextBlock>("PendingRequestCount")!.Text = activeRequests.Length.ToString(CultureInfo.CurrentCulture);
        this.FindControl<TextBlock>("HomeLowStock")!.Text = $"{inventoryRows.Count(x => x.ExpectedScaled <= 0)} خامات تحتاج مراجعة";
        this.FindControl<TextBlock>("HomeRecipes")!.Text = $"{_recipeRows.Length} وصفة منشورة";
        this.FindControl<TextBlock>("HomeRequests")!.Text = $"{activeRequests.Length} طلب قيد التنفيذ";
        this.FindControl<TextBlock>("HomeOrders")!.Text = $"{customOrders.Count(x => x.Status is not (CustomOrderStatus.Delivered or CustomOrderStatus.Cancelled))} طلب مفتوح";
        this.FindControl<TextBlock>("HomeItems")!.Text = $"{itemRows.Count(x => x.Item.Kind == "PRODUCT")} منتج · {ingredientChoices.Length} خامة";
        this.FindControl<TextBlock>("ServerStatusText")!.Text = configuration is null ? "الجهاز غير مربوط بالخادم" : $"مرتبط بـ {configuration.SiteName}";
    }
    private void Home_Click(object? sender, RoutedEventArgs e) => this.FindControl<TabControl>("MainTabs")!.SelectedIndex = 0;
    private void OpenRequests_Click(object? sender, RoutedEventArgs e) => this.FindControl<TabControl>("MainTabs")!.SelectedIndex = 1;
    private void OpenStock_Click(object? sender, RoutedEventArgs e) => this.FindControl<TabControl>("MainTabs")!.SelectedIndex = 3;
    private void OpenRecipes_Click(object? sender, RoutedEventArgs e) => this.FindControl<TabControl>("MainTabs")!.SelectedIndex = 4;
    private void OpenOrders_Click(object? sender, RoutedEventArgs e) => this.FindControl<TabControl>("MainTabs")!.SelectedIndex = 5;
    private void OpenItems_Click(object? sender, RoutedEventArgs e) => this.FindControl<TabControl>("MainTabs")!.SelectedIndex = 6;
    private void OpenSettings_Click(object? sender, RoutedEventArgs e) => this.FindControl<TabControl>("MainTabs")!.SelectedIndex = 7;
    private static SyncUploadEvent? ParseUpload(KitchenOutbox row)
    {
        try
        {
            var upload = JsonSerializer.Deserialize<SyncUploadEvent>(row.UploadJson);
            return upload?.EventType == "shipment.dispatched" ? upload : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
    private void ApplyWaredFilter(Guid? selectedRequestId = null)
    {
        this.FindControl<Button>("PendingFilter")!.Content = $"Pending · قيد الانتظار ({_waredRows.Count(x => x.Status == "PENDING")})";
        this.FindControl<Button>("SentFilter")!.Content = $"Sent · تم الإرسال ({_waredRows.Count(x => x.Status == "SENT")})";
        this.FindControl<Button>("ConfirmedFilter")!.Content = $"Confirmed · مؤكد ({_waredRows.Count(x => x.Status == "CONFIRMED")})";
        this.FindControl<Button>("ConflictedFilter")!.Content = $"Conflicted · مختلف ({_waredRows.Count(x => x.Status == "CONFLICTED")})";
        var filtered = _waredRows.Where(x => x.Status == _waredFilter).OrderByDescending(x => x.SortAt).ToArray();
        RequestsList.ItemsSource = filtered;
        RequestsList.SelectedItem = filtered.FirstOrDefault(x => x.Request.Id == selectedRequestId) ?? filtered.FirstOrDefault();
        if (filtered.Length == 0)
        {
            _selected = null;
            SelectedTitle.Text = "لا توجد طلبات في هذه الحالة";
            this.FindControl<TextBlock>("SelectedSubtitle")!.Text = "ستظهر التغييرات هنا تلقائياً.";
            LinesGrid.ItemsSource = null;
            this.FindControl<Button>("DispatchButton")!.IsVisible = false;
        }
    }
    private void SetWaredFilter(string status)
    {
        _waredFilter = status;
        ApplyWaredFilter();
    }
    private void PendingFilter_Click(object? sender, RoutedEventArgs e) => SetWaredFilter("PENDING");
    private void SentFilter_Click(object? sender, RoutedEventArgs e) => SetWaredFilter("SENT");
    private void ConfirmedFilter_Click(object? sender, RoutedEventArgs e) => SetWaredFilter("CONFIRMED");
    private void ConflictedFilter_Click(object? sender, RoutedEventArgs e) => SetWaredFilter("CONFLICTED");
    private void SetConnectionState(string state)
    {
        var badge = this.FindControl<Border>("SyncBadge")!;
        var text = this.FindControl<TextBlock>("SyncStateText")!;
        (text.Text, text.Foreground, badge.Background) = state switch
        {
            "CONNECTED" => ("Connected · متصل", Brush.Parse("#237A49"), Brush.Parse("#E9F8EF")),
            "SYNCING" => ("Syncing · جارٍ المزامنة", Brush.Parse("#175CD3"), Brush.Parse("#EAF2FF")),
            _ => ("Offline · غير متصل", Brush.Parse("#B42318"), Brush.Parse("#FDECEC"))
        };
    }
    private async void Enroll_Click(object? sender, RoutedEventArgs e) { await Run(async () => { SetConnectionState("SYNCING"); await _sync.EnrollAsync(new Uri(ApiUrl.Text!.Trim()), EnrollmentToken.Text!.Trim(), DeviceName.Text!.Trim()); EnrollmentToken.Text = ""; var applied = await _sync.PullAsync(forceRetry: true); SetConnectionState("CONNECTED"); return $"تم ربط جهاز المطبخ وبدأت المزامنة التلقائية — {applied} تحديث."; }); await RefreshAsync(); }
    private async void Sync_Click(object? sender, RoutedEventArgs e) { SetConnectionState("SYNCING"); await Run(async () => { var applied = await _sync.PullAsync(forceRetry: true); SetConnectionState("CONNECTED"); return $"اكتملت المزامنة؛ تم تطبيق {applied} تحديث."; }); await RefreshAsync(); }
    private async void Update_Click(object? sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null) { await CheckForUpdatesAsync(true); return; }
        await Run(async () =>
        {
            await _sync.PullAsync(forceRetry: true);
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
        if (RequestsList.SelectedItem is not WaredRow row) return;
        _selected = row.Request;
        SelectedTitle.Text = $"{row.BranchName} · {row.Reference}";
        this.FindControl<TextBlock>("SelectedSubtitle")!.Text = row.CanDispatch
            ? "راجع الكمية المطلوبة، عدّل ما سيرسله المطبخ، ثم اضغط Confirm & Send. اكتب 0 للصنف غير المتاح."
            : $"الحالة: {row.Status} · هذه نسخة الشحنة المسجلة ولا يمكن تعديلها.";
        LinesGrid.ItemsSource = row.Request.Lines.Select(x => new DispatchLine(x, row.ShippedQuantities.GetValueOrDefault(x.Id), row.CanDispatch)).ToList();
        LinesGrid.IsReadOnly = !row.CanDispatch;
        this.FindControl<Button>("DispatchButton")!.IsVisible = row.CanDispatch;
    }
    private async void Dispatch_Click(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_selected is null) { StatusText.Text = "اختر طلباً أولاً."; return; }
        var rows = (LinesGrid.ItemsSource as IEnumerable<DispatchLine>)?.ToArray() ?? [];
        var confirm = new Window { Title = "تأكيد إرسال الشحنة", Width = 480, Height = 220, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var send = new Button { Content = "تأكيد الإرسال", IsDefault = true };
        var cancel = new Button { Content = "إلغاء", IsCancel = true };
        send.Click += (_, _) => confirm.Close(true); cancel.Click += (_, _) => confirm.Close(false);
        confirm.Content = new StackPanel { Margin = new Avalonia.Thickness(18), Spacing = 14, Children = { new TextBlock { Text = $"تأكيد إرسال الكميات المحددة إلى {_selected.BranchName}؟", TextWrapping = Avalonia.Media.TextWrapping.Wrap }, send, cancel } };
        if (!await confirm.ShowDialog<bool>(this)) return;
        await Run(async () => { await _sync.DispatchAsync(_selected.Id, rows.ToDictionary(x => x.Id, x => x.SendScaled)); return "تم تأكيد الشحنة وحفظها؛ ستصل للفرع تلقائياً دون تكرار."; });
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
                try { cancellationToken.ThrowIfCancellationRequested(); SetConnectionState("SYNCING"); var applied = await _sync.PullAsync(cancellationToken); await RefreshAsync(); SetConnectionState("CONNECTED"); StatusText.Text = applied == 0 ? "متصل — المزامنة التلقائية تعمل" : $"تمت المزامنة تلقائياً — {applied} تحديث"; }
                catch (InvalidOperationException ex) when (ex.Message.Contains("اربط", StringComparison.Ordinal)) { SetConnectionState("OFFLINE"); StatusText.Text = "اربط الجهاز مرة واحدة؛ بعدها سيكون الاتصال تلقائياً."; }
                catch (Exception ex) { SetConnectionState("OFFLINE"); StatusText.Text = $"Offline — التغييرات محفوظة وستُعاد تلقائياً: {ex.Message}"; }
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
    private async void ReceiveIngredients_Click(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<ComboBox>("PurchaseIngredient")!.SelectedItem is not ProductChoice selected)
        { StatusText.Text = "اختر الخامة أولاً."; return; }
        var reason = this.FindControl<TextBox>("InventoryReason")!.Text ?? "شراء خامات";
        await Run(async () =>
        {
            await using var db = _store.Open();
            var ingredient = await db.Products.AsNoTracking().SingleAsync(x => x.Id == selected.Id && x.Kind == "INGREDIENT");
            var amount = ParseNormalizedQuantity(this.FindControl<TextBox>("PurchaseQuantity")!.Text,
                SelectedUnit("PurchaseUnit"), ingredient);
            var cost = ParseMoneyMinor(this.FindControl<TextBox>("PurchaseCost")!.Text);
            await _sync.ReceiveIngredientPurchaseAsync(selected.Id, amount, cost, reason);
            return "تم تسجيل الشراء؛ حُسب متوسط تكلفة الخامة تلقائياً وسيُزامن مع الخادم.";
        });
        await RefreshAsync();
    }
    private async void WasteIngredients_Click(object? sender, RoutedEventArgs e) { var reason = this.FindControl<TextBox>("InventoryReason")!.Text ?? ""; await Run(async () => { await _sync.RecordWasteAsync(ReadIngredientInputs(false), reason); return "تم تسجيل الهالك وخصمه مرة واحدة."; }); await RefreshAsync(); }
    private async void CountIngredients_Click(object? sender, RoutedEventArgs e) { if (!await ConfirmAsync("إقفال جرد اليوم", "سيتم حفظ الكميات الفعلية وفروق الجرد كسجل غير قابل للتعديل. هل تريد المتابعة؟")) return; await Run(async () => { await _sync.CountIngredientsAsync(ReadIngredientInputs(true)); var report = await new KitchenDailyReportWriter(_store).WriteLatestCountAsync(_reports.GetDirectory()); return $"تم إقفال الجرد وحفظ تقرير Excel غير قابل للاستبدال: {report.Path}"; }); await RefreshAsync(); }
    private static long ParseMoneyMinor(string? text)
    {
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            || value <= 0 || value * 100m != decimal.Truncate(value * 100m))
            throw new InvalidOperationException("أدخل التكلفة الإجمالية بالجنيه، مثل 500 أو 180.50.");
        return checked((long)(value * 100m));
    }
    private string SelectedUnit(string controlName) =>
        (this.FindControl<ComboBox>(controlName)!.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
    private static long ParseNormalizedQuantity(string? text, string enteredUnit, KitchenProduct ingredient)
    {
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            throw new InvalidOperationException("أدخل كمية صحيحة أكبر من الصفر.");
        static decimal Factor(string unit) => unit.Trim().ToLowerInvariant() switch
        {
            "kg" or "كيلو" => 1000m, "g" or "جرام" => 1m,
            "l" or "لتر" => 1000m, "ml" or "مل" => 1m,
            "قطعة" or "pcs" or "piece" or "egg" => 1m,
            _ => throw new InvalidOperationException("استخدم g أو kg أو ml أو L أو قطعة كوحدة للخامة.")
        };
        var itemUnit = ingredient.Unit.Trim().ToLowerInvariant();
        var weight = itemUnit is "g" or "kg" or "جرام" or "كيلو";
        var volume = itemUnit is "ml" or "l" or "مل" or "لتر";
        if (weight && enteredUnit is not ("g" or "kg") || volume && enteredUnit is not ("ml" or "L")
            || !weight && !volume && enteredUnit != "قطعة")
            throw new InvalidOperationException($"وحدة الشراء لا تطابق وحدة {ingredient.Name}.");
        var scaled = amount * Factor(enteredUnit) / Factor(ingredient.Unit) * ingredient.QuantityScale;
        if (scaled != decimal.Truncate(scaled) || scaled > long.MaxValue)
            throw new InvalidOperationException("كمية الشراء أدق من دقة الخامة؛ عدّل وحدة التخزين أو الكمية.");
        return checked((long)scaled);
    }
    private async void RecipeProduct_Selected(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingRecipeChoices || this.FindControl<ComboBox>("RecipeProduct")!.SelectedItem is not ProductChoice selected) return;
        await using var db = _store.Open();
        var recipe = await db.Recipes.AsNoTracking().Include(x => x.Components)
            .SingleOrDefaultAsync(x => x.ProductItemId == selected.Id);
        var product = await db.Products.AsNoTracking().SingleAsync(x => x.Id == selected.Id);
        _recipeExpectedVersion = recipe?.Version ?? 0;
        _recipeDraft.Clear();
        if (recipe is not null) foreach (var line in recipe.Components) _recipeDraft[line.IngredientItemId] = line.QuantityScaled;
        this.FindControl<TextBox>("RecipeOutput")!.Text = recipe is null ? "1"
            : ((decimal)recipe.OutputScaled / product.QuantityScale).ToString("0.###", CultureInfo.InvariantCulture);
        RefreshRecipeDraft(await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id),
            await db.Ingredients.AsNoTracking().ToDictionaryAsync(x => x.ItemId));
    }
    private async void AddRecipeIngredient_Click(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<ComboBox>("RecipeIngredient")!.SelectedItem is not ProductChoice selected)
        { StatusText.Text = "اختر خامة للوصفة."; return; }
        await Run(async () =>
        {
            await using var db = _store.Open();
            var ingredient = await db.Products.AsNoTracking().SingleAsync(x => x.Id == selected.Id && x.Kind == "INGREDIENT");
            _recipeDraft[selected.Id] = ParseNormalizedQuantity(this.FindControl<TextBox>("RecipeQuantity")!.Text,
                SelectedUnit("RecipeUnit"), ingredient);
            RefreshRecipeDraft(await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id),
                await db.Ingredients.AsNoTracking().ToDictionaryAsync(x => x.ItemId));
            return "تمت إضافة الخامة للوصفة؛ احفظ الوصفة لتزامنها.";
        });
    }
    private async void RemoveRecipeIngredient_Click(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<DataGrid>("RecipeComponentsGrid")!.SelectedItem is not RecipeComponentEntry row) return;
        _recipeDraft.Remove(row.ItemId);
        await using var db = _store.Open();
        RefreshRecipeDraft(await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id),
            await db.Ingredients.AsNoTracking().ToDictionaryAsync(x => x.ItemId));
    }
    private async void SaveRecipe_Click(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<ComboBox>("RecipeProduct")!.SelectedItem is not ProductChoice selected)
        { StatusText.Text = "اختر المنتج أولاً."; return; }
        await Run(async () =>
        {
            await using var db = _store.Open();
            var product = await db.Products.AsNoTracking().SingleAsync(x => x.Id == selected.Id);
            if (!decimal.TryParse(this.FindControl<TextBox>("RecipeOutput")!.Text, NumberStyles.Number,
                CultureInfo.InvariantCulture, out var amount) || amount <= 0
                || amount * product.QuantityScale != decimal.Truncate(amount * product.QuantityScale))
                throw new InvalidOperationException("كمية الناتج غير صالحة.");
            await _sync.SaveRecipeAsync(selected.Id, _recipeExpectedVersion,
                checked((long)(amount * product.QuantityScale)), new Dictionary<Guid, long>(_recipeDraft));
            _recipeExpectedVersion++;
            return "حُفظت الوصفة وستصل للخادم تلقائياً.";
        });
        await RefreshAsync();
    }
    private void RefreshRecipeDraft(IReadOnlyDictionary<Guid, KitchenProduct> products,
        IReadOnlyDictionary<Guid, KitchenIngredientBalance> balances)
    {
        var rows = _recipeDraft.Where(x => products.ContainsKey(x.Key)).Select(x =>
            new RecipeComponentEntry(products[x.Key], balances.GetValueOrDefault(x.Key), x.Value)).ToArray();
        this.FindControl<DataGrid>("RecipeComponentsGrid")!.ItemsSource = rows;
        this.FindControl<TextBlock>("RecipeCost")!.Text = $"تكلفة الوصفة: {rows.Sum(x => x.CostMinor) / 100m:0.00} ج.م";
    }
    private void NewProduct_Click(object? sender, RoutedEventArgs e) => NewItem("PRODUCT");
    private void NewIngredient_Click(object? sender, RoutedEventArgs e) => NewItem("INGREDIENT");
    private void NewItem(string kind)
    {
        _selectedItem = null;
        this.FindControl<DataGrid>("ProductsGrid")!.SelectedItem = null;
        this.FindControl<DataGrid>("IngredientsGrid")!.SelectedItem = null;
        this.FindControl<TextBox>("ItemName")!.Text = "";
        this.FindControl<TextBox>("ItemUnit")!.Text = kind == "PRODUCT" ? "قطعة" : "g";
        this.FindControl<TextBox>("ItemScale")!.Text = "1";
        this.FindControl<TextBox>("ItemBasePrice")!.Text = "0";
        this.FindControl<ComboBox>("ItemKind")!.SelectedIndex = kind == "PRODUCT" ? 0 : 1;
    }
    private void ItemKind_Changed(object? sender, SelectionChangedEventArgs e)
    {
        var kind = (this.FindControl<ComboBox>("ItemKind")?.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var price = this.FindControl<TextBox>("ItemBasePrice");
        if (price is not null) price.IsEnabled = kind == "PRODUCT";
    }
    private void Item_Selected(object? sender, SelectionChangedEventArgs e)
    {
        if ((sender as DataGrid)?.SelectedItem is not KitchenItemRow row) return;
        _selectedItem = row.Item;
        this.FindControl<TextBox>("ItemName")!.Text = row.Item.Name;
        this.FindControl<TextBox>("ItemUnit")!.Text = row.Item.Unit;
        this.FindControl<TextBox>("ItemScale")!.Text = row.Item.QuantityScale.ToString(CultureInfo.InvariantCulture);
        this.FindControl<ComboBox>("ItemKind")!.SelectedIndex = row.Item.Kind == "INGREDIENT" ? 1 : 0;
        this.FindControl<TextBox>("ItemBasePrice")!.Text = (row.Item.BasePriceMinor / 100m).ToString("0.00", CultureInfo.InvariantCulture);
    }
    private async void SaveItem_Click(object? sender, RoutedEventArgs e)
    {
        var name = this.FindControl<TextBox>("ItemName")!.Text ?? "";
        var unit = this.FindControl<TextBox>("ItemUnit")!.Text ?? "";
        if (!int.TryParse(this.FindControl<TextBox>("ItemScale")!.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var scale))
        { StatusText.Text = "دقة الوحدة غير صالحة."; return; }
        var selector = this.FindControl<ComboBox>("ItemKind")!;
        var kind = (selector.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "PRODUCT";
        if (!decimal.TryParse(this.FindControl<TextBox>("ItemBasePrice")!.Text, NumberStyles.Number,
                CultureInfo.InvariantCulture, out var price) || price < 0 || price * 100m != decimal.Truncate(price * 100m))
        { StatusText.Text = "سعر البيع غير صالح."; return; }
        KitchenProduct? saved = null;
        await Run(async () =>
        {
            saved = await _sync.SaveCatalogItemAsync(_selectedItem?.Id, _selectedItem?.Version, name, unit, scale,
                kind, checked((long)(price * 100m)));
            return "تم حفظ الصنف محلياً؛ ستتم مزامنته تلقائياً مع الخادم والفروع.";
        });
        if (saved is not null) _selectedItem = saved;
        await RefreshAsync();
    }
    private async void CafeCustomer_Selected(object? sender, SelectionChangedEventArgs e)
    {
        if (this.FindControl<ComboBox>("CafeCustomer")!.SelectedItem is not CafeCustomerOption customer) return;
        await using var db = _store.Open();
        var selectedCafe = await db.CafeCustomers.AsNoTracking().SingleAsync(x => x.Id == customer.Id);
        _selectedCafeId = customer.Id; _selectedCafeVersion = selectedCafe.Version;
        this.FindControl<TextBox>("CafeName")!.Text = selectedCafe.Name;
        this.FindControl<TextBox>("CafeContact")!.Text = selectedCafe.Contact;
        var prices = await db.CafePrices.AsNoTracking().Include(x => x.Item).Where(x => x.CustomerId == customer.Id && x.Item.Active).OrderBy(x => x.Item.Name).ToListAsync();
        var availableProducts = await db.Products.AsNoTracking().Where(x => x.Active && x.Kind == "PRODUCT").OrderBy(x => x.Name).ToListAsync();
        this.FindControl<DataGrid>("CafeItemsGrid")!.ItemsSource = availableProducts.Select(product =>
            new CafeItemEntry(prices.FirstOrDefault(x => x.ItemId == product.Id)
                ?? new KitchenCafePrice { CustomerId = customer.Id, ItemId = product.Id,
                    Item = product, UnitPriceMinor = product.BasePriceMinor, Version = 1 })).ToList();
        this.FindControl<TextBox>("CustomDue")!.Text = DateTime.Now.AddDays(1).ToString("yyyy-MM-dd 12:00", CultureInfo.InvariantCulture);
    }
    private void NewCafe_Click(object? sender, RoutedEventArgs e)
    {
        _selectedCafeId = null; _selectedCafeVersion = 0;
        this.FindControl<ComboBox>("CafeCustomer")!.SelectedItem = null;
        this.FindControl<TextBox>("CafeName")!.Text = "";
        this.FindControl<TextBox>("CafeContact")!.Text = "";
        this.FindControl<DataGrid>("CafeItemsGrid")!.ItemsSource = null;
    }
    private async void SaveCafe_Click(object? sender, RoutedEventArgs e)
    {
        KitchenCafeCustomer? saved = null;
        await Run(async () =>
        {
            saved = await _sync.SaveCafeAsync(_selectedCafeId,
                _selectedCafeId is null ? null : _selectedCafeVersion,
                this.FindControl<TextBox>("CafeName")!.Text ?? "",
                this.FindControl<TextBox>("CafeContact")!.Text ?? "");
            return "حُفظ الكافيه محلياً وسيظهر تلقائياً في الخادم والفروع.";
        });
        if (saved is not null) { _selectedCafeId = saved.Id; _selectedCafeVersion = saved.Version; }
        await RefreshAsync();
    }
    private async void DeleteCafeCustomer_Click(object? sender, RoutedEventArgs e)
    {
        var selector = this.FindControl<ComboBox>("CafeCustomer")!;
        if (selector.SelectedItem is not CafeCustomerOption customer) { StatusText.Text = "اختر الكافيه أولاً."; return; }
        if (!await ConfirmAsync("حذف الكافيه", $"سيتم إخفاء {customer.Name} من الطلبات الجديدة مع الاحتفاظ بكل فواتيره السابقة. هل تريد المتابعة؟")) return;
        await Run(async () => { await _sync.HideCafeCustomerAsync(customer.Id); return $"تم حذف {customer.Name} من قائمة الكافيهات، والفواتير السابقة محفوظة."; });
        this.FindControl<DataGrid>("CafeItemsGrid")!.ItemsSource = null;
        await RefreshAsync();
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
    private async void DeliverCafeOrder_Click(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<DataGrid>("CustomOrdersGrid")!.SelectedItem is not KitchenOrderRow row)
        { StatusText.Text = "اختر طلب الكافيه أولاً."; return; }
        if (!await ConfirmAsync("تأكيد تجهيز وتسليم الطلب", $"سيتم خصم خامات وصفات {row.OrderNumber} مرة واحدة. هل تؤكد التسليم؟")) return;
        await Run(async () => { await _sync.DeliverCafeOrderAsync(row.Id); return "تم تسليم الطلب وخصم خاماته مرة واحدة؛ ستصل الحركة للخادم تلقائياً."; });
        await RefreshAsync();
    }
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
    private readonly KitchenIngredientBalance _item; public IngredientEntry(KitchenIngredientBalance item, long expectedScaled) { _item = item; ExpectedScaled = expectedScaled; }
    public Guid ItemId => _item.ItemId; public string Name => _item.Name; public long ExpectedScaled { get; } public string CurrentDisplay => $"{(decimal)_item.QuantityScaled / _item.QuantityScale:0.###} {_item.Unit}"; public string ExpectedDisplay => $"{(decimal)ExpectedScaled / _item.QuantityScale:0.###} {_item.Unit}"; public string StockStatus => ExpectedScaled < 0 ? "عجز" : ExpectedScaled == 0 ? "ينفد" : "متاح"; public string InputDisplay { get; set; } = "";
    public string AverageCostDisplay => _item.QuantityScaled <= 0 ? "—" : $"{(decimal)_item.InventoryCostMinor / _item.QuantityScaled * _item.QuantityScale / 100m:0.###} ج.م / {_item.Unit}";
    public long InputScaled => decimal.TryParse(InputDisplay, out var value) && value >= 0 && decimal.Truncate(value * _item.QuantityScale) == value * _item.QuantityScale ? checked((long)(value * _item.QuantityScale)) : throw new InvalidOperationException($"كمية {_item.Name} غير صالحة.");
}
public sealed class KitchenItemRow(KitchenProduct item, KitchenIngredientBalance? balance,
    KitchenRecipeRecord? recipe, IReadOnlyDictionary<Guid, KitchenIngredientBalance> ingredients)
{
    public KitchenProduct Item { get; } = item;
    public string Name => Item.Name;
    public string KindDisplay => Item.Kind == "INGREDIENT" ? "خامة" : "منتج";
    public string UnitDisplay => $"{Item.Unit} × {Item.QuantityScale}";
    public int Version => Item.Version;
    public string BasePriceDisplay => Item.Kind == "PRODUCT" ? $"{Item.BasePriceMinor / 100m:0.00} ج.م" : "—";
    public string CostDisplay => Item.Kind == "INGREDIENT"
        ? balance is null || balance.QuantityScaled <= 0 ? "—" : $"{(decimal)balance.InventoryCostMinor / balance.QuantityScaled * Item.QuantityScale / 100m:0.###} ج.م"
        : recipe is null ? "لا توجد وصفة" : $"{recipe.Components.Sum(x => ingredients.TryGetValue(x.IngredientItemId, out var stock) && stock.QuantityScaled > 0 ? (decimal)stock.InventoryCostMinor * x.QuantityScaled / stock.QuantityScaled : 0m) / 100m:0.00} ج.م";
}
public sealed record ProductChoice(Guid Id, string Name) { public override string ToString() => Name; }
public sealed class RecipeComponentEntry(KitchenProduct ingredient, KitchenIngredientBalance? balance, long quantityScaled)
{
    public Guid ItemId => ingredient.Id;
    public string Name => ingredient.Name;
    public string QuantityDisplay => $"{(decimal)quantityScaled / ingredient.QuantityScale:0.###} {ingredient.Unit}";
    public long CostMinor => balance is null || balance.QuantityScaled <= 0 ? 0
        : checked((long)decimal.Round((decimal)balance.InventoryCostMinor * quantityScaled / balance.QuantityScaled, 0, MidpointRounding.AwayFromZero));
    public string CostDisplay => $"{CostMinor / 100m:0.00} ج.م";
}
public sealed class RecipeRow
{
    public string ProductName { get; init; } = "";
    public string OutputDisplay { get; init; } = "";
    public string ComponentsDisplay { get; init; } = "";
    public int Version { get; init; }
}
public sealed class WaredRow
{
    private WaredRow(KitchenRequestRecord request, string status, string reference, DateTimeOffset sortAt, IReadOnlyDictionary<Guid, long> shippedQuantities)
    {
        Request = request;
        Status = status;
        Reference = reference;
        SortAt = sortAt;
        ShippedQuantities = shippedQuantities;
    }
    public KitchenRequestRecord Request { get; }
    public string BranchName => Request.BranchName;
    public string Status { get; }
    public string Reference { get; }
    public DateTimeOffset SortAt { get; }
    public string SubmittedDisplay => SortAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
    public string ItemCountDisplay => $"{Request.Lines.Count} أصناف";
    public bool CanDispatch => Status == "PENDING";
    public IReadOnlyDictionary<Guid, long> ShippedQuantities { get; }
    public static WaredRow Create(KitchenRequestRecord request, SyncUploadEvent? shipment, IReadOnlyCollection<KitchenReceiptRecord> receipts)
    {
        if (shipment is null)
            return new WaredRow(request, "PENDING", $"REQ-{request.Id.ToString("N")[..8].ToUpperInvariant()}", request.SubmittedAtUtc, new Dictionary<Guid, long>());
        var payload = shipment.Payload;
        var shipmentId = payload.GetProperty("shipment_id").GetGuid();
        var receipt = receipts.FirstOrDefault(x => x.ShipmentId == shipmentId);
        var status = receipt?.Status switch { "ACCEPTED" => "CONFIRMED", "DISPUTED" => "CONFLICTED", _ => "SENT" };
        var quantities = payload.GetProperty("lines").EnumerateArray().ToDictionary(
            x => x.GetProperty("request_line_id").GetGuid(),
            x => ReadLong(x.GetProperty("sent_scaled")));
        var reference = payload.TryGetProperty("reference", out var referenceValue) ? referenceValue.GetString() ?? shipmentId.ToString("N") : shipmentId.ToString("N");
        var occurredAt = DateTimeOffset.TryParse(shipment.OccurredAt, out var parsed) ? parsed : request.SubmittedAtUtc;
        return new WaredRow(request, status, reference, occurredAt, quantities);
    }
    private static long ReadLong(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? long.Parse(value.GetString()!, CultureInfo.InvariantCulture)
        : value.GetInt64();
}
public sealed class DispatchLine
{
    private readonly KitchenRequestLineRecord _line;
    public DispatchLine(KitchenRequestLineRecord line, long shippedScaled, bool editable) { _line = line; var amount = editable ? line.RequestedScaled - line.SentScaled : shippedScaled; SendDisplay = ((decimal)Math.Max(0, amount) / line.QuantityScale).ToString("0.###"); }
    public Guid Id => _line.Id; public string Name => _line.Name; public string RequestedDisplay => $"{(decimal)_line.RequestedScaled / _line.QuantityScale:0.###} {_line.Unit}"; public string SentDisplay => $"{(decimal)_line.SentScaled / _line.QuantityScale:0.###} {_line.Unit}"; public string SendDisplay { get; set; }
    public string ChangeDisplay { get { try { var sent = SendScaled; return sent == 0 ? "غير متاح" : sent == _line.RequestedScaled ? "مطابق" : sent < _line.RequestedScaled ? "أقل من المطلوب" : "أعلى من المطلوب"; } catch { return "راجع الكمية"; } } }
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
    public KitchenOrderRow(CustomOrderSnapshot order) { Id = order.Id; OrderNumber = order.OrderNumber; CustomerName = order.CustomerName; DueDisplay = order.DueAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"); TotalDisplay = $"{order.TotalMinor / 100m:0.00} ج.م"; StatusDisplay = order.Status.ToString(); }
    public Guid Id { get; } public string OrderNumber { get; } public string CustomerName { get; } public string DueDisplay { get; } public string TotalDisplay { get; } public string StatusDisplay { get; }
}
