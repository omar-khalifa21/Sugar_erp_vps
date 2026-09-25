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
using Avalonia.LogicalTree;
using Avalonia.Data;
using SugarERP.Desktop.Shared;

namespace SugarERP.Kitchen.App;
public sealed partial class MainWindow : Window
{
    private readonly KitchenStore _store; private readonly KitchenSyncService _sync; private KitchenRequestRecord? _selected;
    private KitchenProduct? _selectedItem;
    private Guid? _selectedCafeId;
    private int _selectedCafeVersion;
    private readonly Dictionary<Guid, long> _recipeDraft = [];
    private TextBlock? _recipeSavedUnitText;
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
    private KitchenItemRow[] _ingredientRows = [];
    private KitchenItemRow[] _productRows = [];
    private CafeCustomerOption[] _cafeRows = [];
    private CafeItemEntry[] _cafeProductRows = [];
    private readonly Dictionary<Guid, CafeItemEntry> _currentCafeOrder = [];
    private string _waredSearch = "";
    private DataGrid? _wasteHistoryGrid;
    private TextBlock? _wastePeriodText;
    private TextBlock? _wasteSummaryText;
    private int _wasteMonthOffset;
    private int _lastPendingSyncCount = -1;
    private readonly ReportDirectorySettings _reports = new("Kitchen");
    private Button SyncButton => this.FindControl<Button>("SyncButton")!;
    private TextBlock StatusText => this.FindControl<TextBlock>("StatusText")!;
    private ListBox RequestsList => this.FindControl<ListBox>("RequestsList")!;
    private TextBlock SelectedTitle => this.FindControl<TextBlock>("SelectedTitle")!;
    private DataGrid LinesGrid => this.FindControl<DataGrid>("LinesGrid")!;
    private TextBox ApiUrl => this.FindControl<TextBox>("ApiUrl")!;
    private TextBox EnrollmentToken => this.FindControl<TextBox>("EnrollmentToken")!;
    private TextBox DeviceName => this.FindControl<TextBox>("DeviceName")!;
    public MainWindow() { InitializeComponent(); InstallIngredientPriceAction(); InstallRecipeSavedUnitDisplay(); InstallListSearches(); InstallWasteHistoryUi(); InstallHomeIcons(); _store = null!; _sync = null!; }
    public MainWindow(KitchenStore store, KitchenSyncService sync) { InitializeComponent(); InstallIngredientPriceAction(); InstallRecipeSavedUnitDisplay(); InstallListSearches(); InstallWasteHistoryUi(); InstallHomeIcons(); _store = store; _sync = sync; DeviceName.Text = Environment.MachineName; ApiUrl.Text = _deployment.ApiBaseUrl.ToString().TrimEnd('/'); this.FindControl<TextBox>("ReportsDirectory")!.Text = _reports.GetDirectory(); this.FindControl<ComboBox>("PrinterName")!.ItemsSource = new WindowsRasterBranchPrinter().GetInstalledPrinterNames(); Opened += async (_, _) => { SetConnectionState("SYNCING"); await RefreshAsync(); _ = RunBackgroundSyncAsync(_backgroundStop.Token); _ = RunPeriodicUpdateChecksAsync(_backgroundStop.Token); }; Closed += (_, _) => { _backgroundStop.Cancel(); _updateHttp.Dispose(); }; }

    private void InstallWasteHistoryUi()
    {
        var toggle = this.FindControl<Button>("IngredientStockToggle")!;
        if (toggle.Parent is StackPanel togglePanel)
        {
            var waste = new Button { Content = "إتلاف خامة", Margin = new Avalonia.Thickness(8, 0, 0, 0), MinWidth = 135 };
            waste.Classes.Add("danger");
            waste.Click += WasteIngredientDialog_Click;
            togglePanel.Children.Add(waste);
        }
        var panel = this.FindControl<Grid>("IngredientStockPanel")!;
        panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var previous = new Button { Content = "الشهر السابق" };
        var current = new Button { Content = "الشهر الحالي" };
        previous.Click += async (_, _) => { _wasteMonthOffset--; await RefreshAsync(); };
        current.Click += async (_, _) => { _wasteMonthOffset = 0; await RefreshAsync(); };
        _wastePeriodText = new TextBlock { FontSize = 18, FontWeight = FontWeight.Bold, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        _wasteSummaryText = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Avalonia.Thickness(0, 8, 0, 8) };
        _wasteHistoryGrid = new DataGrid { AutoGenerateColumns = false, CanUserSortColumns = false, IsReadOnly = true, MaxHeight = 235 };
        _wasteHistoryGrid.Columns.Add(new DataGridTextColumn { Header = "الخامة", Binding = new Binding(nameof(WasteHistoryRow.Ingredient)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _wasteHistoryGrid.Columns.Add(new DataGridTextColumn { Header = "الكمية", Binding = new Binding(nameof(WasteHistoryRow.Quantity)), Width = new DataGridLength(150) });
        _wasteHistoryGrid.Columns.Add(new DataGridTextColumn { Header = "السبب", Binding = new Binding(nameof(WasteHistoryRow.Reason)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _wasteHistoryGrid.Columns.Add(new DataGridTextColumn { Header = "التاريخ والوقت", Binding = new Binding(nameof(WasteHistoryRow.Occurred)), Width = new DataGridLength(190) });
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        heading.Children.Add(_wastePeriodText);
        var controls = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6, Children = { previous, current } };
        Grid.SetColumn(controls, 1); heading.Children.Add(controls);
        var content = new StackPanel { Children = { heading, _wasteSummaryText, _wasteHistoryGrid } };
        var border = new Border { Classes = { "panel" }, Margin = new Avalonia.Thickness(0, 12, 0, 0), Child = content };
        Grid.SetRow(border, 2); panel.Children.Add(border);
    }
    private void InstallHomeIcons()
    {
        var icons = new Dictionary<string, string> { ["الخامات"] = "◈", ["الوصفات"] = "▤", ["الوارد"] = "⇩", ["الكافيهات"] = "☕", ["المنتجات"] = "◇", ["الإعدادات"] = "⚙" };
        foreach (var text in this.GetLogicalDescendants().OfType<TextBlock>())
            if (text.GetLogicalAncestors().OfType<Button>().Any(x => x.Classes.Contains("home-card"))
                && text.Text is string label && icons.TryGetValue(label, out var icon)) text.Text = $"{icon}  {label}";
    }
    private void InstallListSearches()
    {
        AddListSearch(this.FindControl<ListBox>("IngredientCatalogList")!, "بحث في الخامات...", text =>
            this.FindControl<ListBox>("IngredientCatalogList")!.ItemsSource = _ingredientRows.Where(x => Matches(x.Name, text)).ToArray());
        AddListSearch(this.FindControl<DataGrid>("ProductsGrid")!, "بحث في المنتجات...", text =>
            this.FindControl<DataGrid>("ProductsGrid")!.ItemsSource = _productRows.Where(x => Matches(x.Name, text)).ToArray());
        AddListSearch(this.FindControl<ListBox>("CafeCustomer")!, "بحث باسم الكافيه أو الهاتف...", text =>
            this.FindControl<ListBox>("CafeCustomer")!.ItemsSource = _cafeRows.Where(x => Matches(x.Name, text) || Matches(x.Contact, text)).ToArray());
        AddListSearch(this.FindControl<ListBox>("RequestsList")!, "بحث في الوارد...", text => { _waredSearch = text; ApplyWaredFilter(); });
    }
    private static bool Matches(string value, string search) => string.IsNullOrWhiteSpace(search)
        || value.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase);
    private static void AddListSearch(Control list, string placeholder, Action<string> filter)
    {
        if (list.Parent is not Grid grid) return;
        var search = new TextBox { PlaceholderText = placeholder, Margin = new Avalonia.Thickness(0, 0, 0, 8) };
        search.TextChanged += (_, _) => filter(search.Text ?? "");
        if (grid.RowDefinitions.Count >= 2)
        {
            grid.RowDefinitions.Insert(1, new RowDefinition(GridLength.Auto)); Grid.SetRow(search, 1); Grid.SetRow(list, 2);
        }
        else
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            Grid.SetRow(search, 0); Grid.SetColumn(search, Grid.GetColumn(list)); Grid.SetRow(list, 1);
            foreach (var child in grid.Children.Where(x => x != list && Grid.GetColumn(x) != Grid.GetColumn(list))) Grid.SetRowSpan(child, 2);
        }
        grid.Children.Add(search);
    }
    private void InstallIngredientPriceAction()
    {
        var hint = this.FindControl<TextBlock>("IngredientPricingHint")!;
        if (hint.Parent is not StackPanel panel) return;
        var button = new Button { Content = "تحديث سعر الخامة", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        button.Classes.Add("secondary");
        button.Click += AddPriceForSelectedIngredient_Click;
        panel.Children.Insert(panel.Children.IndexOf(hint) + 1, button);
        var remove = panel.Children.OfType<Button>().FirstOrDefault(x => x.Classes.Contains("danger"));
        if (remove is not null) remove.Content = "أرشفة الخامة وإخفاؤها";
    }
    private void InstallRecipeSavedUnitDisplay()
    {
        var selector = this.FindControl<ComboBox>("RecipeUnit")!;
        if (selector.Parent is not Panel panel) return;
        var index = panel.Children.IndexOf(selector);
        selector.IsVisible = false;
        _recipeSavedUnitText = new TextBlock { Text = "الوحدة: —", MinWidth = 110,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, FontWeight = FontWeight.SemiBold };
        panel.Children.Insert(index, _recipeSavedUnitText);
        if (panel is Grid) Grid.SetColumn(_recipeSavedUnitText, 2);
        this.FindControl<ComboBox>("RecipeIngredient")!.SelectionChanged += RecipeIngredient_Selected;
    }
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
        var ingredients = await db.Ingredients.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        var ingredientsById = ingredients.ToDictionary(x => x.ItemId);
        var recipes = await db.Recipes.AsNoTracking().Include(x => x.Components).ToListAsync();
        var recipesByProduct = recipes.ToDictionary(x => x.ProductItemId);
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id);
        var cafePricesByProduct = (await db.CafePrices.AsNoTracking().ToListAsync()).GroupBy(x => x.ItemId)
            .ToDictionary(x => x.Key, x => x.Select(p => p.UnitPriceMinor).Distinct().OrderBy(p => p).ToArray());
        var selectedItemId = _selectedItem?.Id;
        var productRows = products.Values.Where(x => x.Active && x.Kind == "PRODUCT").OrderBy(x => x.Name)
            .Select(x => new KitchenItemRow(x, recipesByProduct.GetValueOrDefault(x.Id), ingredientsById,
                cafePricesByProduct.GetValueOrDefault(x.Id) ?? [])).ToArray();
        var ingredientRows = ingredients.Where(x => x.Active).Select(x => new KitchenItemRow(x)).ToArray();
        _productRows = productRows; _ingredientRows = ingredientRows;
        var itemRows = productRows.Concat(ingredientRows).ToArray();
        this.FindControl<DataGrid>("ProductsGrid")!.ItemsSource = productRows;
        this.FindControl<ListBox>("IngredientCatalogList")!.ItemsSource = ingredientRows;
        if (selectedItemId is Guid selectedId)
        {
            var selectedRow = itemRows.FirstOrDefault(x => x.Item.Id == selectedId);
            if (selectedRow is not null)
                if (selectedRow.Item.Kind == "INGREDIENT")
                    this.FindControl<ListBox>("IngredientCatalogList")!.SelectedItem = selectedRow;
                else
                    this.FindControl<DataGrid>("ProductsGrid")!.SelectedItem = selectedRow;
        }
        var selectedRecipeProductId = (this.FindControl<ComboBox>("RecipeProduct")!.SelectedItem as ProductChoice)?.Id;
        _loadingRecipeChoices = true;
        this.FindControl<ComboBox>("RecipeProduct")!.ItemsSource = products.Values.Where(x => x.Active && x.Kind == "PRODUCT")
            .OrderBy(x => x.Name).Select(x => new ProductChoice(x.Id, x.Name)).ToArray();
        this.FindControl<ComboBox>("RecipeProduct")!.SelectedItem = ((IEnumerable<ProductChoice>)this.FindControl<ComboBox>("RecipeProduct")!.ItemsSource!)
            .FirstOrDefault(x => x.Id == selectedRecipeProductId);
        var ingredientChoices = ingredients.Where(x => x.Active)
            .Select(x => new ProductChoice(x.ItemId, x.Name)).ToArray();
        this.FindControl<ComboBox>("RecipeIngredient")!.ItemsSource = ingredientChoices;
        _loadingRecipeChoices = false;
        RefreshRecipeDraft(ingredientsById);
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
        this.FindControl<ListBox>("InventoryCards")!.ItemsSource = inventoryRows;
        RefreshWasteHistory(ingredients, await db.IngredientMovements.AsNoTracking().Where(x => x.Kind == "WASTE").ToListAsync());
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
        var customOrders = await _sync.GetCustomOrdersAsync();
        var cafeOptions = (await db.CafeCustomers.AsNoTracking().Where(x => x.Active && !x.HiddenLocally).OrderBy(x => x.Name).ToListAsync())
            .Select(x => new CafeCustomerOption(x.Id, x.Name, x.Contact,
                customOrders.Where(order => order.CafeCustomerId == x.Id).Sum(order => order.RemainingMinor),
                customOrders.Count(order => order.CafeCustomerId == x.Id && order.Status is not (CustomOrderStatus.Delivered or CustomOrderStatus.Cancelled)))).ToArray();
        _cafeRows = cafeOptions;
        this.FindControl<ListBox>("CafeCustomer")!.ItemsSource = cafeOptions;
        this.FindControl<ListBox>("CafeCustomer")!.SelectedItem = cafeOptions.FirstOrDefault(x => x.Id == _selectedCafeId);
        this.FindControl<DataGrid>("CustomOrdersGrid")!.ItemsSource = customOrders
            .Where(x => _selectedCafeId is null || x.CafeCustomerId == _selectedCafeId)
            .Select(x => new KitchenOrderRow(x)).ToArray();
        var configuration = await db.Configuration.AsNoTracking().SingleOrDefaultAsync();
        this.FindControl<StackPanel>("EnrollmentPanel")!.IsVisible = configuration is null || configuration.DeviceId == Guid.Empty;
        this.FindControl<Button>("ResetConnectionButton")!.IsVisible = configuration is not null && configuration.DeviceId != Guid.Empty;
        if (configuration is not null && !string.IsNullOrWhiteSpace(configuration.PrinterName)) this.FindControl<ComboBox>("PrinterName")!.SelectedItem = configuration.PrinterName;
        var pendingSync = await db.Outbox.AsNoTracking().CountAsync(x => !x.Acknowledged);
        _lastPendingSyncCount = pendingSync;
        this.FindControl<TextBlock>("PendingSyncCount")!.Text = pendingSync.ToString(CultureInfo.CurrentCulture);
        this.FindControl<TextBlock>("IngredientCount")!.Text = ingredients.Count.ToString(CultureInfo.CurrentCulture);
        this.FindControl<TextBlock>("PendingRequestCount")!.Text = activeRequests.Length.ToString(CultureInfo.CurrentCulture);
        this.FindControl<TextBlock>("HomeLowStock")!.Text = $"{inventoryRows.Count(x => x.ExpectedScaled <= 0)} خامات تحتاج مراجعة";
        this.FindControl<TextBlock>("HomeRecipes")!.Text = $"{_recipeRows.Length} وصفة منشورة";
        this.FindControl<TextBlock>("HomeRequests")!.Text = $"{activeRequests.Length} طلب قيد التنفيذ";
        this.FindControl<TextBlock>("HomeOrders")!.Text = $"{customOrders.Count(x => x.Status is not (CustomOrderStatus.Delivered or CustomOrderStatus.Cancelled))} طلب مفتوح";
        this.FindControl<TextBlock>("HomeItems")!.Text = $"{productRows.Length} منتج · {ingredientChoices.Length} خامة";
        this.FindControl<TextBlock>("ServerStatusText")!.Text = configuration is null ? "الجهاز غير مربوط بالخادم" : $"مرتبط بـ {configuration.SiteName}";
    }
    private void Home_Click(object? sender, RoutedEventArgs e) => this.FindControl<TabControl>("MainTabs")!.SelectedIndex = 0;
    private void OpenRequests_Click(object? sender, RoutedEventArgs e) => this.FindControl<TabControl>("MainTabs")!.SelectedIndex = 1;
    private void OpenStock_Click(object? sender, RoutedEventArgs e) => this.FindControl<TabControl>("MainTabs")!.SelectedIndex = 2;
    private void OpenRecipes_Click(object? sender, RoutedEventArgs e) => this.FindControl<TabControl>("MainTabs")!.SelectedIndex = 3;
    private void OpenOrders_Click(object? sender, RoutedEventArgs e) => this.FindControl<TabControl>("MainTabs")!.SelectedIndex = 4;
    private void OpenItems_Click(object? sender, RoutedEventArgs e) => this.FindControl<TabControl>("MainTabs")!.SelectedIndex = 5;
    private void OpenSettings_Click(object? sender, RoutedEventArgs e) => this.FindControl<TabControl>("MainTabs")!.SelectedIndex = 6;
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
        var filtered = _waredRows.Where(x => x.Status == _waredFilter && (Matches(x.BranchName, _waredSearch) || Matches(x.Reference, _waredSearch))).OrderByDescending(x => x.SortAt).ToArray();
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
            "RETRYING" => ("Retrying · إعادة المحاولة", Brush.Parse("#9A6700"), Brush.Parse("#FFF4CE")),
            "AUTH" => ("Authentication problem · مشكلة مصادقة", Brush.Parse("#B42318"), Brush.Parse("#FDECEC")),
            _ => ("Offline · غير متصل", Brush.Parse("#B42318"), Brush.Parse("#FDECEC"))
        };
    }
    private async void Enroll_Click(object? sender, RoutedEventArgs e) { await Run(async () => { SetConnectionState("SYNCING"); await _sync.EnrollAsync(new Uri(ApiUrl.Text!.Trim()), EnrollmentToken.Text!.Trim(), DeviceName.Text!.Trim()); EnrollmentToken.Text = ""; var applied = await _sync.PullAsync(forceRetry: true); SetConnectionState("CONNECTED"); return $"تم ربط جهاز المطبخ وبدأت المزامنة التلقائية — {applied} تحديث."; }); await RefreshAsync(); }
    private async void ResetConnection_Click(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmAsync("إلغاء ربط الجهاز", "سيتم حذف بيانات الاتصال والمصادقة فقط. لن تُحذف المنتجات أو الخامات أو الفواتير. لا يمكن المتابعة إذا توجد عمليات لم تُزامن. هل تريد المتابعة؟")) return;
        await Run(async () => { await _sync.ResetConnectionAsync(); SetConnectionState("OFFLINE"); return "تم إلغاء الربط بأمان. أدخل رمز تسجيل جديد للاتصال من جديد."; });
        await RefreshAsync();
    }
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
    private async Task Run(Func<Task<string>> action) { if (_busy) return; _busy = true; try { SyncButton.IsEnabled = false; StatusText.Text = "جارٍ تنفيذ العملية..."; StatusText.Text = await action(); } catch (DbUpdateConcurrencyException) { StatusText.Text = "تم تحديث البيانات أثناء عملك. أُعيد تحميل أحدث نسخة؛ راجع التغييرات ثم احفظ مرة أخرى."; await RefreshAsync(); } catch (DbUpdateException) { StatusText.Text = "تعذر حفظ التغيير بأمان. أُعيد تحميل أحدث البيانات؛ حاول مرة أخرى."; await RefreshAsync(); } catch (Exception ex) { StatusText.Text = ex.Message; } finally { _busy = false; SyncButton.IsEnabled = true; } }
    private async Task RunBackgroundSyncAsync(CancellationToken cancellationToken)
    {
        var consecutiveFailures = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var delay = TimeSpan.FromSeconds(2);
                if (!_busy)
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        SetConnectionState("SYNCING");
                        var applied = await _sync.PullAsync(cancellationToken);
                        await using var statusDb = _store.Open();
                        var pending = await statusDb.Outbox.AsNoTracking().CountAsync(x => !x.Acknowledged, cancellationToken);
                        if (applied > 0 || pending != _lastPendingSyncCount) await RefreshAsync();
                        consecutiveFailures = 0;
                        SetConnectionState("CONNECTED");
                        StatusText.Text = applied == 0 ? "متصل — المزامنة التلقائية تعمل" : $"تمت المزامنة تلقائياً — {applied} تحديث";
                        delay = TimeSpan.FromSeconds(20);
                    }
                    catch (BusinessRuleException ex) when (ex.Code == "DEVICE_NOT_ENROLLED")
                    {
                        consecutiveFailures = 0;
                        SetConnectionState("OFFLINE");
                        StatusText.Text = "اربط الجهاز مرة واحدة؛ بعدها سيكون الاتصال والمزامنة تلقائيين.";
                        delay = TimeSpan.FromSeconds(20);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                    catch (CentralApiException ex) when (ex.StatusCode is 401 or 403 || !ex.Retryable)
                    {
                        consecutiveFailures = 0;
                        SetConnectionState("AUTH");
                        StatusText.Text = $"مشكلة مصادقة ({ex.Code}). عطّل الجهاز القديم من الإدارة، ثم استخدم إلغاء ربط الجهاز ورمز تسجيل جديد. البيانات المحلية محفوظة.";
                        delay = TimeSpan.FromMinutes(2);
                        System.Diagnostics.Trace.WriteLine($"[SYNC] Kitchen authentication failure {ex.Code}: {ex}");
                    }
                    catch (Exception ex)
                    {
                        consecutiveFailures++;
                        var seconds = Math.Min(120, 5 * Math.Pow(2, Math.Min(consecutiveFailures - 1, 5)));
                        delay = TimeSpan.FromMilliseconds(seconds * 1000 + Random.Shared.Next(250, 1750));
                        SetConnectionState(consecutiveFailures >= 3 ? "OFFLINE" : "RETRYING");
                        StatusText.Text = consecutiveFailures >= 3
                            ? $"Offline — البيانات محفوظة؛ المحاولة التالية خلال {delay.TotalSeconds:0} ثانية: {ex.Message}"
                            : $"تعذر اتصال واحد؛ البيانات محفوظة وتتم إعادة المحاولة تلقائياً خلال {delay.TotalSeconds:0} ثانية.";
                        System.Diagnostics.Trace.WriteLine($"[SYNC] Kitchen automatic sync failure {consecutiveFailures}; retry in {delay}: {ex}");
                    }
                }
                await Task.Delay(delay, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
    private async void ReportsFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Excel & Reports Location", AllowMultiple = false });
        if (folders.Count == 0) return;
        await Run(() => { var path = folders[0].TryGetLocalPath() ?? throw new IOException("اختر مجلداً محلياً."); _reports.SaveDirectory(path); this.FindControl<TextBox>("ReportsDirectory")!.Text = path; return Task.FromResult("تم حفظ مجلد التقارير؛ الملفات السابقة لم تتغير."); });
    }
    private IReadOnlyDictionary<Guid, long> ReadIngredientInputs(bool requireAll) { var rows = (this.FindControl<DataGrid>("InventoryGrid")!.ItemsSource as IEnumerable<IngredientEntry>)?.ToArray() ?? []; return rows.Where(x => requireAll || !string.IsNullOrWhiteSpace(x.InputDisplay)).ToDictionary(x => x.ItemId, x => x.InputScaled); }
    private void ShowIngredientCatalog_Click(object? sender, RoutedEventArgs e) => SetIngredientView(false);
    private void ShowIngredientStock_Click(object? sender, RoutedEventArgs e) => SetIngredientView(true);
    private void SetIngredientView(bool stock)
    {
        this.FindControl<Grid>("IngredientCatalogPanel")!.IsVisible = !stock;
        this.FindControl<Grid>("IngredientStockPanel")!.IsVisible = stock;
        this.FindControl<Button>("IngredientCatalogToggle")!.Classes.Set("primary", !stock);
        this.FindControl<Button>("IngredientStockToggle")!.Classes.Set("primary", stock);
    }
    private async void AddIngredient_Click(object? sender, RoutedEventArgs e)
    {
        var name = new TextBox { PlaceholderText = "اسم الخامة، مثل دقيق أو بيض" };
        var unit = new ComboBox { ItemsSource = new[] { "قطعة", "g", "kg", "ml", "L" }, SelectedIndex = 0 };
        var quantity = new TextBox { PlaceholderText = "كمية التسعير والرصيد الافتتاحي", Text = "1" };
        var price = new TextBox { PlaceholderText = "سعر شراء هذه الكمية بالجنيه" };
        var dialog = CreateEntryDialog("إضافة خامة", out var save, out var cancel,
            new TextBlock { Text = "تُنشأ الخامة مع أول عملية شراء في معاملة واحدة. الوحدة المحفوظة ستُستخدم في كل المشتريات التالية.", TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "اسم الخامة" }, name, new TextBlock { Text = "الوحدة" }, unit,
            new TextBlock { Text = "الكمية التي اشتريتها" }, quantity,
            new TextBlock { Text = "إجمالي سعر الشراء" }, price);
        save.Click += (_, _) => dialog.Close(true); cancel.Click += (_, _) => dialog.Close(false);
        if (!await dialog.ShowDialog<bool>(this)) return;
        await Run(async () =>
        {
            var selectedUnit = unit.SelectedItem?.ToString() ?? "";
            var scale = UnitScale(selectedUnit);
            var amount = ParseQuantityScaled(quantity.Text, scale);
            await _sync.CreateIngredientWithOpeningPurchaseAsync(name.Text ?? "", selectedUnit, scale, amount, ParseMoneyMinor(price.Text));
            return "تم إنشاء الخامة وتسجيل أول شراء؛ حُفظت الوحدة والتكلفة والرصيد وستتم المزامنة تلقائياً.";
        });
        await RefreshAsync();
    }
    private async void AddPurchase_Click(object? sender, RoutedEventArgs e) => await OpenPurchaseDialogAsync(null);
    private async void AddPriceForSelectedIngredient_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedItem?.Kind != "INGREDIENT") { StatusText.Text = "اختر خامة مثل Eggs أولاً."; return; }
        var pricingQuantity = new TextBox { PlaceholderText = "كمية التسعير، مثل 30", Text = "1" };
        var pricingPrice = new TextBox { PlaceholderText = "سعر كمية التسعير بالجنيه، مثل 120" };
        var preview = new TextBlock { Text = "تكلفة الوحدة: —", FontWeight = FontWeight.Bold, Foreground = Brush.Parse("#75264A") };
        void UpdatePreview()
        {
            preview.Text = decimal.TryParse(pricingQuantity.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity)
                && decimal.TryParse(pricingPrice.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) && quantity > 0 && price > 0
                ? $"تكلفة الوحدة: {price / quantity:0.###} ج.م / {_selectedItem.Unit}" : "تكلفة الوحدة: —";
        }
        pricingQuantity.TextChanged += (_, _) => UpdatePreview(); pricingPrice.TextChanged += (_, _) => UpdatePreview();
        var dialog = CreateEntryDialog("تحديث سعر الخامة", out var save, out var cancel,
            new TextBlock { Text = $"{_selectedItem.Name} · الوحدة: {_selectedItem.Unit}\nيُطبق السعر الجديد على المشتريات والوصفات المستقبلية دون تغيير الحركات التاريخية.", TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "كمية التسعير" }, pricingQuantity, new TextBlock { Text = "سعر كمية التسعير" }, pricingPrice, preview);
        save.Click += (_, _) => dialog.Close(true); cancel.Click += (_, _) => dialog.Close(false);
        if (!await dialog.ShowDialog<bool>(this)) return;
        if (!decimal.TryParse(pricingQuantity.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var quantityValue) || quantityValue <= 0
            || !decimal.TryParse(pricingPrice.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var priceValue) || priceValue <= 0)
        { StatusText.Text = "أدخل كمية تسعير وسعراً صحيحين."; return; }
        var value = priceValue / quantityValue;
        await Run(async () =>
        {
            await _sync.UpdateIngredientPurchaseUnitCostAsync(_selectedItem.Id,
                checked((long)decimal.Round(value * 1_000_000m, 0, MidpointRounding.AwayFromZero)));
            return "تم تحديث سعر الخامة. سيُستخدم تلقائياً في المشتريات المستقبلية فقط.";
        });
        await RefreshAsync();
    }
    private async Task OpenPurchaseDialogAsync(Guid? preferredIngredientId)
    {
        await using var db = _store.Open();
        var ingredients = await db.Ingredients.AsNoTracking().Where(x => x.Active).OrderBy(x => x.Name).ToArrayAsync();
        if (ingredients.Length == 0) { StatusText.Text = "أضف خامة أولاً قبل تسجيل المشتريات."; return; }
        var choices = ingredients.Select(x => new IngredientPurchaseChoice(x)).ToArray();
        var ingredient = new ComboBox { ItemsSource = choices };
        ingredient.SelectedItem = choices.FirstOrDefault(x => x.Ingredient.ItemId == preferredIngredientId) ?? choices[0];
        var savedUnit = new TextBlock { FontWeight = FontWeight.Bold };
        void UpdateUnit() => savedUnit.Text = ingredient.SelectedItem is IngredientPurchaseChoice row
            ? $"الوحدة: {row.Ingredient.Unit}\nتكلفة الوحدة: {row.Ingredient.PurchaseUnitCostMicros / 1_000_000m:0.###} ج.م" : "";
        ingredient.SelectionChanged += (_, _) => UpdateUnit(); UpdateUnit();
        var quantity = new TextBox { PlaceholderText = "الكمية", Text = "1" };
        var reason = new TextBox { PlaceholderText = "مرجع أو ملاحظة اختيارية", Text = "شراء خامات" };
        var calculation = new TextBlock { Text = "أدخل الكمية لحساب إجمالي التكلفة.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#75264A"), FontWeight = FontWeight.SemiBold };
        void UpdateCalculation()
        {
            if (ingredient.SelectedItem is not IngredientPurchaseChoice selectedChoice)
            { calculation.Text = "اختر الخامة."; return; }
            if (selectedChoice.Ingredient.PurchaseUnitCostMicros <= 0)
            { calculation.Text = "حدّث سعر الخامة أولاً من شاشة بيانات الخامة."; return; }
            if (!decimal.TryParse(quantity.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var enteredQuantity)
                || enteredQuantity <= 0)
            { calculation.Text = "أدخل الكمية لحساب إجمالي التكلفة."; return; }
            var row = selectedChoice.Ingredient;
            var total = enteredQuantity * row.PurchaseUnitCostMicros / 1_000_000m;
            calculation.Text = $"إجمالي التكلفة: {total:0.00} ج.م";
        }
        quantity.TextChanged += (_, _) => UpdateCalculation();
        ingredient.SelectionChanged += (_, _) => UpdateCalculation();
        var dialog = CreateEntryDialog("إضافة مشتريات", out var save, out var cancel,
            new TextBlock { Text = "اختر خامة موجودة. الوحدة للعرض فقط ولا يمكن تغييرها أثناء الشراء.", TextWrapping = TextWrapping.Wrap },
            ingredient, savedUnit, new TextBlock { Text = "الكمية" }, quantity, calculation, reason);
        save.Click += (_, _) => dialog.Close(true); cancel.Click += (_, _) => dialog.Close(false);
        if (!await dialog.ShowDialog<bool>(this) || ingredient.SelectedItem is not IngredientPurchaseChoice choice) return;
        var selected = choice.Ingredient;
        await Run(async () =>
        {
            var total = await _sync.ReceiveIngredientPurchaseAtSavedCostAsync(selected.ItemId,
                ParseQuantityScaled(quantity.Text, selected.QuantityScale), reason.Text ?? "شراء خامات");
            return $"تمت إضافة المخزون بتكلفة محسوبة {total / 100m:0.00} ج.م؛ سيُزامن السجل تلقائياً.";
        });
        await RefreshAsync();
    }
    private static Window CreateEntryDialog(string title, out Button save, out Button cancel, params Control[] fields)
    {
        var dialog = new Window { Title = title, Width = 510, MinHeight = 430, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, FlowDirection = FlowDirection.RightToLeft };
        save = new Button { Content = "حفظ", IsDefault = true, MinWidth = 130 };
        cancel = new Button { Content = "إلغاء", IsCancel = true, MinWidth = 100 };
        var body = new StackPanel { Margin = new Avalonia.Thickness(22), Spacing = 9 };
        foreach (var field in fields) body.Children.Add(field);
        body.Children.Add(new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Children = { save, cancel } });
        dialog.Content = new ScrollViewer { Content = body };
        return dialog;
    }
    private static int UnitScale(string unit) => unit is "kg" or "L" ? 1000 : 1;
    private static long ParseQuantityScaled(string? text, int scale)
    {
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) || value <= 0
            || value * scale != decimal.Truncate(value * scale))
            throw new InvalidOperationException("أدخل كمية صحيحة متوافقة مع الوحدة المحفوظة.");
        return checked((long)(value * scale));
    }
    private async void WasteIngredientDialog_Click(object? sender, RoutedEventArgs e)
    {
        await using var db = _store.Open();
        var choices = (await db.Ingredients.AsNoTracking().Where(x => x.Active).OrderBy(x => x.Name).ToListAsync())
            .Select(x => new IngredientWasteChoice(x)).ToArray();
        if (choices.Length == 0) { StatusText.Text = "لا توجد خامات نشطة لإتلافها."; return; }
        var ingredient = new ComboBox { ItemsSource = choices, PlaceholderText = "اختر الخامة" };
        var unit = new TextBlock { Text = "الوحدة: —", FontWeight = FontWeight.SemiBold };
        var available = new TextBlock { Text = "المتاح: —", Classes = { "muted" } };
        var quantity = new TextBox { PlaceholderText = "الكمية التالفة" };
        var reason = new TextBox { PlaceholderText = "السبب، مثل: انتهت الصلاحية", MaxLength = 500 };
        ingredient.SelectionChanged += (_, _) =>
        {
            if (ingredient.SelectedItem is not IngredientWasteChoice selected) return;
            unit.Text = $"الوحدة: {selected.Ingredient.Unit}";
            available.Text = $"المتاح: {(decimal)selected.Ingredient.QuantityScaled / selected.Ingredient.QuantityScale:0.###} {selected.Ingredient.Unit}";
        };
        var dialog = CreateEntryDialog("إتلاف خامة", out var save, out var cancel,
            new TextBlock { Text = "يسجل الهالك كحركة مخزون دائمة ولا يحذف الخامة أو مشترياتها.", TextWrapping = TextWrapping.Wrap },
            ingredient, unit, available, new TextBlock { Text = "الكمية التالفة" }, quantity,
            new TextBlock { Text = "السبب" }, reason);
        save.Content = "إضافة الهالك";
        save.Click += (_, _) => dialog.Close(true); cancel.Click += (_, _) => dialog.Close(false);
        if (!await dialog.ShowDialog<bool>(this) || ingredient.SelectedItem is not IngredientWasteChoice choice) return;
        await Run(async () =>
        {
            await _sync.RecordWasteAsync(new Dictionary<Guid, long>
                { [choice.Ingredient.ItemId] = ParseQuantityScaled(quantity.Text, choice.Ingredient.QuantityScale) }, reason.Text ?? "");
            return "تم تسجيل الهالك وخصمه من المخزون؛ سيُزامن السجل تلقائياً.";
        });
        await RefreshAsync();
    }
    private void WasteIngredients_Click(object? sender, RoutedEventArgs e) => WasteIngredientDialog_Click(sender, e);

    private void RefreshWasteHistory(IReadOnlyCollection<KitchenIngredientBalance> ingredients, IReadOnlyCollection<KitchenIngredientMovement> movements)
    {
        if (_wasteHistoryGrid is null || _wastePeriodText is null || _wasteSummaryText is null) return;
        var cairo = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Egypt Standard Time" : "Africa/Cairo");
        var localStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(_wasteMonthOffset);
        var localEnd = localStart.AddMonths(1);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, cairo);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(localEnd, cairo);
        var byId = ingredients.ToDictionary(x => x.ItemId);
        var rows = movements.Where(x => x.OccurredAtUtc >= startUtc && x.OccurredAtUtc < endUtc)
            .OrderByDescending(x => x.OccurredAtUtc)
            .Select(x => new WasteHistoryRow(x, byId.GetValueOrDefault(x.IngredientItemId), cairo)).ToArray();
        _wastePeriodText.Text = $"سجل الهالك — {localStart:yyyy/MM}";
        _wasteHistoryGrid.ItemsSource = rows;
        var grouped = rows.GroupBy(x => new { x.IngredientId, x.Ingredient, x.Unit })
            .Select(x => $"{x.Key.Ingredient}: {x.Sum(y => y.QuantityValue):0.###} {x.Key.Unit}");
        var quantities = rows.Length == 0 ? "لا يوجد هالك في هذا الشهر." : string.Join("  •  ", grouped);
        _wasteSummaryText.Text = $"{quantities}\nإجمالي تكلفة الهالك: {rows.Sum(x => x.CostMinor) / 100m:0.00} ج.م";
    }
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
    private static long ParseNormalizedQuantity(string? text, string enteredUnit, KitchenIngredientBalance ingredient)
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
        RefreshRecipeDraft(await db.Ingredients.AsNoTracking().ToDictionaryAsync(x => x.ItemId));
    }
    private async void RecipeIngredient_Selected(object? sender, SelectionChangedEventArgs e)
    {
        if (_recipeSavedUnitText is null || this.FindControl<ComboBox>("RecipeIngredient")!.SelectedItem is not ProductChoice selected)
        { if (_recipeSavedUnitText is not null) _recipeSavedUnitText.Text = "الوحدة: —"; return; }
        await using var db = _store.Open();
        var ingredient = await db.Ingredients.AsNoTracking().SingleOrDefaultAsync(x => x.ItemId == selected.Id && x.Active);
        _recipeSavedUnitText.Text = ingredient is null ? "الوحدة: —" : $"الوحدة: {ingredient.Unit}";
    }
    private async void AddRecipeIngredient_Click(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<ComboBox>("RecipeIngredient")!.SelectedItem is not ProductChoice selected)
        { StatusText.Text = "اختر خامة للوصفة."; return; }
        await Run(async () =>
        {
            await using var db = _store.Open();
            var ingredient = await db.Ingredients.AsNoTracking().SingleAsync(x => x.ItemId == selected.Id && x.Active);
            _recipeDraft[selected.Id] = ParseNormalizedQuantity(this.FindControl<TextBox>("RecipeQuantity")!.Text,
                ingredient.Unit, ingredient);
            RefreshRecipeDraft(await db.Ingredients.AsNoTracking().ToDictionaryAsync(x => x.ItemId));
            return "تمت إضافة الخامة للوصفة؛ احفظ الوصفة لتزامنها.";
        });
    }
    private async void RemoveRecipeIngredient_Click(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<DataGrid>("RecipeComponentsGrid")!.SelectedItem is not RecipeComponentEntry row) return;
        _recipeDraft.Remove(row.ItemId);
        await using var db = _store.Open();
        RefreshRecipeDraft(await db.Ingredients.AsNoTracking().ToDictionaryAsync(x => x.ItemId));
    }
    private async void RecipeComponentEdited(object? sender, DataGridCellEditEndedEventArgs e)
    {
        if (e.Row.DataContext is not RecipeComponentEntry row) return;
        try { _recipeDraft[row.ItemId] = row.QuantityScaled; }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        await using var db = _store.Open();
        RefreshRecipeDraft(await db.Ingredients.AsNoTracking().ToDictionaryAsync(x => x.ItemId));
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
    private void RefreshRecipeDraft(IReadOnlyDictionary<Guid, KitchenIngredientBalance> balances)
    {
        var rows = _recipeDraft.Where(x => balances.ContainsKey(x.Key)).Select(x =>
            new RecipeComponentEntry(balances[x.Key], x.Value)).ToArray();
        this.FindControl<DataGrid>("RecipeComponentsGrid")!.ItemsSource = rows;
        this.FindControl<TextBlock>("RecipeCost")!.Text = $"{rows.Sum(x => x.CostMinor) / 100m:0.00} ج.م";
    }
    private void NewProduct_Click(object? sender, RoutedEventArgs e)
    {
        _selectedItem = null;
        this.FindControl<DataGrid>("ProductsGrid")!.SelectedItem = null;
        this.FindControl<TextBox>("ProductName")!.Text = "";
        this.FindControl<TextBox>("ProductUnit")!.Text = "قطعة";
        this.FindControl<TextBox>("ProductScale")!.Text = "1";
        this.FindControl<TextBox>("ItemBasePrice")!.Text = "0";
    }
    private void Item_Selected(object? sender, SelectionChangedEventArgs e)
    {
        var row = sender switch
        {
            DataGrid grid => grid.SelectedItem as KitchenItemRow,
            ListBox list => list.SelectedItem as KitchenItemRow,
            _ => null
        };
        if (row is null) return;
        _selectedItem = row.Item;
        if (row.Item.Kind == "INGREDIENT")
        {
            this.FindControl<TextBox>("ItemName")!.Text = row.Item.Name;
            var unit = this.FindControl<ComboBox>("ItemUnit")!;
            unit.SelectedItem = unit.Items.Cast<ComboBoxItem>().FirstOrDefault(x => x.Content?.ToString() == row.Item.Unit);
            this.FindControl<TextBlock>("IngredientPricingHint")!.Text = $"متوسط التكلفة الحالي: {row.CostDisplay}";
        }
        else
        {
            this.FindControl<TextBox>("ProductName")!.Text = row.Item.Name;
            this.FindControl<TextBox>("ProductUnit")!.Text = row.Item.Unit;
            this.FindControl<TextBox>("ProductScale")!.Text = row.Item.QuantityScale.ToString(CultureInfo.InvariantCulture);
            this.FindControl<TextBox>("ItemBasePrice")!.Text = (row.Item.BasePriceMinor / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
    private async void SaveProduct_Click(object? sender, RoutedEventArgs e)
    {
        var name = this.FindControl<TextBox>("ProductName")!.Text ?? "";
        var unit = this.FindControl<TextBox>("ProductUnit")!.Text ?? "";
        if (!int.TryParse(this.FindControl<TextBox>("ProductScale")!.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var scale))
        { StatusText.Text = "دقة الوحدة غير صالحة."; return; }
        if (!decimal.TryParse(this.FindControl<TextBox>("ItemBasePrice")!.Text, NumberStyles.Number,
                CultureInfo.InvariantCulture, out var price) || price < 0 || price * 100m != decimal.Truncate(price * 100m))
        { StatusText.Text = "سعر البيع غير صالح."; return; }
        KitchenProduct? saved = null;
        await Run(async () =>
        {
            var selected = _selectedItem?.Kind == "PRODUCT" ? _selectedItem : null;
            saved = await _sync.SaveCatalogItemAsync(selected?.Id, selected?.Version, name, unit, scale,
                "PRODUCT", checked((long)(price * 100m)));
            return "تم حفظ المنتج محلياً؛ ستتم مزامنته تلقائياً مع الخادم والفروع.";
        });
        if (saved is not null) _selectedItem = saved;
        await RefreshAsync();
    }
    private async void SaveIngredientMetadata_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedItem?.Kind != "INGREDIENT") { StatusText.Text = "اختر خامة من دليل الخامات أولاً."; return; }
        var unit = (this.FindControl<ComboBox>("ItemUnit")!.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        KitchenProduct? saved = null;
        await Run(async () =>
        {
            saved = await _sync.SaveCatalogItemAsync(_selectedItem.Id, _selectedItem.Version,
                this.FindControl<TextBox>("ItemName")!.Text ?? "", unit, _selectedItem.QuantityScale, "INGREDIENT");
            return "تم حفظ بيانات الخامة؛ لم يتغير الرصيد أو متوسط التكلفة.";
        });
        if (saved is not null) _selectedItem = saved;
        await RefreshAsync();
    }
    private async void PermanentlyDeleteItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedItem is null) { StatusText.Text = "اختر منتجاً أو خامة أولاً."; return; }
        if (!await ConfirmAsync("أرشفة الخامة", $"ستُخفى {_selectedItem.Name} من القوائم والوصفات الجديدة مع الاحتفاظ بالمشتريات والوصفات والسجل التاريخي. هل تريد المتابعة؟")) return;
        var deletedName = _selectedItem.Name;
        await Run(async () =>
        {
            await _sync.ArchiveCatalogItemAsync(_selectedItem.Id);
            _selectedItem = null;
            return $"تمت أرشفة {deletedName} وإخفاؤها مع الاحتفاظ بسجلها التاريخي.";
        });
        await RefreshAsync();
    }
    private async void CafeCustomer_Selected(object? sender, SelectionChangedEventArgs e)
    {
        if (this.FindControl<ListBox>("CafeCustomer")!.SelectedItem is not CafeCustomerOption customer) return;
        if (_selectedCafeId == customer.Id && _cafeProductRows.Length > 0) return;
        await using var db = _store.Open();
        var selectedCafe = await db.CafeCustomers.AsNoTracking().SingleAsync(x => x.Id == customer.Id);
        _selectedCafeId = customer.Id; _selectedCafeVersion = selectedCafe.Version;
        this.FindControl<TextBox>("CafeName")!.Text = selectedCafe.Name;
        this.FindControl<TextBox>("CafeContact")!.Text = selectedCafe.Contact;
        var prices = await db.CafePrices.AsNoTracking().Include(x => x.Item).Where(x => x.CustomerId == customer.Id && x.Item.Active).OrderBy(x => x.Item.Name).ToListAsync();
        var availableProducts = await db.Products.AsNoTracking().Where(x => x.Active && x.Kind == "PRODUCT").OrderBy(x => x.Name).ToListAsync();
        _cafeProductRows = availableProducts.Select(product =>
            new CafeItemEntry(prices.FirstOrDefault(x => x.ItemId == product.Id)
                ?? new KitchenCafePrice { CustomerId = customer.Id, ItemId = product.Id,
                    Item = product, UnitPriceMinor = product.BasePriceMinor, Version = 1 })).ToArray();
        _currentCafeOrder.Clear();
        this.FindControl<ListBox>("CafeProductList")!.ItemsSource = _cafeProductRows;
        RefreshCafeOrderDraft();
        this.FindControl<TextBox>("CustomDue")!.Text = DateTime.Now.AddDays(1).ToString("yyyy-MM-dd 12:00", CultureInfo.InvariantCulture);
    }
    private void CafeProductSearch_Changed(object? sender, TextChangedEventArgs e)
    {
        var search = this.FindControl<TextBox>("CafeProductSearch")!.Text ?? "";
        this.FindControl<ListBox>("CafeProductList")!.ItemsSource = _cafeProductRows.Where(x => Matches(x.Name, search)).ToArray();
    }
    private void CafeProduct_Selected(object? sender, SelectionChangedEventArgs e)
    {
        var list = this.FindControl<ListBox>("CafeProductList")!;
        if (list.SelectedItem is not CafeItemEntry selected) return;
        list.SelectedItem = null;
        if (selected.UnitPriceMinor <= 0) { StatusText.Text = $"لا يوجد سعر كافيه محفوظ للمنتج {selected.Name}. احفظ سعر العميل أولاً."; return; }
        if (_currentCafeOrder.TryGetValue(selected.ItemId, out var existing))
            existing.QuantityText = (existing.QuantityScaled / (decimal)existing.QuantityScale + 1m).ToString("0.###", CultureInfo.InvariantCulture);
        else
        {
            selected.QuantityText = "1";
            _currentCafeOrder[selected.ItemId] = selected;
        }
        RefreshCafeOrderDraft();
    }
    private void CafeOrderLineEdited(object? sender, DataGridCellEditEndedEventArgs e) => RefreshCafeOrderDraft();
    private void RemoveCafeOrderLine_Click(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<DataGrid>("CafeItemsGrid")!.SelectedItem is CafeItemEntry selected)
            _currentCafeOrder.Remove(selected.ItemId);
        RefreshCafeOrderDraft();
    }
    private void RefreshCafeOrderDraft()
    {
        var rows = _currentCafeOrder.Values.ToArray();
        this.FindControl<DataGrid>("CafeItemsGrid")!.ItemsSource = rows;
        long total = 0;
        try { total = rows.Sum(x => x.LineTotalMinor); }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        this.FindControl<TextBlock>("CafeOrderTotal")!.Text = $"{total / 100m:0.00} ج.م";
    }
    private void NewCafe_Click(object? sender, RoutedEventArgs e)
    {
        _selectedCafeId = null; _selectedCafeVersion = 0;
        this.FindControl<ListBox>("CafeCustomer")!.SelectedItem = null;
        this.FindControl<TextBox>("CafeName")!.Text = "";
        this.FindControl<TextBox>("CafeContact")!.Text = "";
        this.FindControl<DataGrid>("CafeItemsGrid")!.ItemsSource = null;
        this.FindControl<ListBox>("CafeProductList")!.ItemsSource = null;
        _cafeProductRows = []; _currentCafeOrder.Clear(); RefreshCafeOrderDraft();
    }
    private async void SaveCafe_Click(object? sender, RoutedEventArgs e)
    {
        KitchenCafeCustomer? saved = null;
        await Run(async () =>
        {
            saved = await _sync.SaveCafeAsync(_selectedCafeId,
                _selectedCafeId is null ? null : _selectedCafeVersion,
                this.FindControl<TextBox>("CafeName")!.Text ?? "",
                this.FindControl<TextBox>("CafeContact")!.Text ?? "",
                _cafeProductRows.ToDictionary(x => x.ItemId, x => x.UnitPriceMinor));
            return "حُفظ الكافيه محلياً وسيظهر تلقائياً في الخادم والفروع.";
        });
        if (saved is not null) { _selectedCafeId = saved.Id; _selectedCafeVersion = saved.Version; }
        await RefreshAsync();
    }
    private async void ArchiveCafeCustomer_Click(object? sender, RoutedEventArgs e)
    {
        var selector = this.FindControl<ListBox>("CafeCustomer")!;
        if (selector.SelectedItem is not CafeCustomerOption customer) { StatusText.Text = "اختر الكافيه أولاً."; return; }
        if (!await ConfirmAsync("أرشفة الكافيه", $"سيتم إيقاف {customer.Name} في كل الأجهزة مع الاحتفاظ بكل فواتيره السابقة. هل تريد المتابعة؟")) return;
        await Run(async () => { await _sync.ArchiveCafeCustomerAsync(customer.Id); return $"تمت أرشفة {customer.Name}، والفواتير السابقة محفوظة."; });
        this.FindControl<DataGrid>("CafeItemsGrid")!.ItemsSource = null;
        await RefreshAsync();
    }
    private async void CreateCustomOrder_Click(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<ListBox>("CafeCustomer")!.SelectedItem is not CafeCustomerOption customer) { StatusText.Text = "اختر العميل أولاً."; return; }
        if (!DateTime.TryParseExact(this.FindControl<TextBox>("CustomDue")!.Text?.Trim(), "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dueLocal)) { StatusText.Text = "اكتب موعد التسليم بهذا الشكل: 2026-09-20 18:30"; return; }
        var rows = _currentCafeOrder.Values.ToArray();
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
public sealed class KitchenItemRow
{
    private readonly KitchenRecipeRecord? _recipe;
    private readonly IReadOnlyDictionary<Guid, KitchenIngredientBalance> _ingredients;
    private readonly KitchenIngredientBalance? _ingredient;
    private readonly long[] _cafePrices;
    public KitchenItemRow(KitchenProduct item, KitchenRecipeRecord? recipe,
        IReadOnlyDictionary<Guid, KitchenIngredientBalance> ingredients, long[]? cafePrices = null)
    {
        Item = item;
        _recipe = recipe;
        _ingredients = ingredients;
        _cafePrices = cafePrices ?? [];
    }
    public KitchenItemRow(KitchenIngredientBalance ingredient)
    {
        _ingredient = ingredient;
        _ingredients = new Dictionary<Guid, KitchenIngredientBalance>();
        _cafePrices = [];
        Item = new KitchenProduct { Id = ingredient.ItemId, Sku = ingredient.Sku, Name = ingredient.Name,
            Unit = ingredient.Unit, Kind = "INGREDIENT", QuantityScale = ingredient.QuantityScale,
            Active = ingredient.Active, Version = ingredient.Version, UpdatedAtUtc = ingredient.UpdatedAtUtc };
    }
    public KitchenProduct Item { get; }
    public string Name => Item.Name;
    public string KindDisplay => Item.Kind == "INGREDIENT" ? "خامة" : "منتج";
    public string UnitDisplay => $"الوحدة: {Item.Unit}";
    public string StockDisplay => _ingredient is null ? "" : $"الرصيد الحالي: {(decimal)_ingredient.QuantityScaled / _ingredient.QuantityScale:0.###} {_ingredient.Unit}";
    public int Version => Item.Version;
    public string BasePriceDisplay => Item.Kind == "PRODUCT" ? $"{Item.BasePriceMinor / 100m:0.00} ج.م" : "—";
    private long? RecipeCostMinor => _recipe is null ? null : checked((long)decimal.Round(_recipe.Components.Sum(x =>
        _ingredients.TryGetValue(x.IngredientItemId, out var stock) ? (decimal)x.QuantityScaled * stock.PurchaseUnitCostMicros / stock.QuantityScale / 10_000m : 0m), 0, MidpointRounding.AwayFromZero));
    public string CafePriceDisplay => _cafePrices.Length == 0 ? "غير محدد" : _cafePrices.Length == 1
        ? $"{_cafePrices[0] / 100m:0.00} ج.م" : $"{_cafePrices[0] / 100m:0.00}–{_cafePrices[^1] / 100m:0.00} ج.م";
    public string ProfitDisplay => RecipeCostMinor is long cost ? $"{(Item.BasePriceMinor - cost) / 100m:0.00} ج.م" : "—";
    public string CafeProfitDisplay => RecipeCostMinor is long cost && _cafePrices.Length == 1 ? $"{(_cafePrices[0] - cost) / 100m:0.00} ج.م" : "—";
    public string CostDisplay => Item.Kind == "INGREDIENT"
        ? _ingredient is null || _ingredient.QuantityScaled <= 0 ? "—" : $"{(decimal)_ingredient.InventoryCostMinor / _ingredient.QuantityScaled * Item.QuantityScale / 100m:0.###} ج.م"
        : _recipe is null ? "لا توجد وصفة" : $"{_recipe.Components.Sum(x => _ingredients.TryGetValue(x.IngredientItemId, out var stock) ? (decimal)x.QuantityScaled * stock.PurchaseUnitCostMicros / stock.QuantityScale / 10_000m : 0m) / 100m:0.00} ج.م";
}
public sealed record ProductChoice(Guid Id, string Name) { public override string ToString() => Name; }
public sealed class RecipeComponentEntry(KitchenIngredientBalance ingredient, long quantityScaled)
{
    public Guid ItemId => ingredient.ItemId;
    public string Name => ingredient.Name;
    public string QuantityDisplay => $"{(decimal)quantityScaled / ingredient.QuantityScale:0.###} {ingredient.Unit}";
    public string QuantityText { get; set; } = ((decimal)quantityScaled / ingredient.QuantityScale).ToString("0.###", CultureInfo.InvariantCulture);
    public long QuantityScaled => decimal.TryParse(QuantityText, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
        && value > 0 && value * ingredient.QuantityScale == decimal.Truncate(value * ingredient.QuantityScale)
        ? checked((long)(value * ingredient.QuantityScale)) : throw new InvalidOperationException($"كمية {ingredient.Name} غير صالحة.");
    public long CostMinor => checked((long)decimal.Round((decimal)ingredient.PurchaseUnitCostMicros * quantityScaled
        / ingredient.QuantityScale / 10_000m, 0, MidpointRounding.AwayFromZero));
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
public sealed record IngredientPurchaseChoice(KitchenIngredientBalance Ingredient)
{
    public override string ToString() => Ingredient.Name;
}
public sealed record IngredientWasteChoice(KitchenIngredientBalance Ingredient)
{
    public override string ToString() => Ingredient.Name;
}
public sealed class WasteHistoryRow
{
    public WasteHistoryRow(KitchenIngredientMovement movement, KitchenIngredientBalance? ingredient, TimeZoneInfo timezone)
    {
        IngredientId = movement.IngredientItemId;
        Ingredient = ingredient?.Name ?? movement.IngredientItemId.ToString();
        Unit = ingredient?.Unit ?? "";
        var scale = ingredient?.QuantityScale is > 0 ? ingredient.QuantityScale : 1;
        QuantityValue = (decimal)Math.Abs(movement.DeltaScaled) / scale;
        Quantity = $"{QuantityValue:0.###} {Unit}".Trim();
        Reason = movement.Reason;
        Occurred = TimeZoneInfo.ConvertTime(movement.OccurredAtUtc, timezone).ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
        CostMinor = movement.CostMinor;
    }
    public Guid IngredientId { get; }
    public string Ingredient { get; }
    public string Unit { get; }
    public decimal QuantityValue { get; }
    public string Quantity { get; }
    public string Reason { get; }
    public string Occurred { get; }
    public long CostMinor { get; }
}
public sealed record CafeCustomerOption(Guid Id, string Name, string Contact, long BalanceMinor, int OpenOrderCount)
{
    public string BalanceDisplay => $"الرصيد: {BalanceMinor / 100m:0.00} ج.م";
    public string OrdersDisplay => OpenOrderCount == 0 ? "لا توجد طلبات مفتوحة" : $"{OpenOrderCount} طلب مفتوح";
    public override string ToString() => Name;
}
public sealed class CafeItemEntry
{
    private readonly KitchenCafePrice _price; public CafeItemEntry(KitchenCafePrice price) { _price = price; PriceText = (price.UnitPriceMinor / 100m).ToString("0.00", CultureInfo.InvariantCulture); }
    public Guid ItemId => _price.ItemId; public string Name => _price.Item.Name; public string PriceDisplay => $"{_price.UnitPriceMinor / 100m:0.00} ج.م"; public string PriceText { get; set; } public string QuantityText { get; set; } = "0";
    public string Unit => _price.Item.Unit; public int QuantityScale => _price.Item.QuantityScale;
    public long UnitPriceMinor => decimal.TryParse(PriceText, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) && price >= 0 && price * 100m == decimal.Truncate(price * 100m) ? checked((long)(price * 100m)) : throw new InvalidOperationException($"سعر الكافيه للمنتج {Name} غير صالح.");
    public long QuantityScaled => decimal.TryParse(QuantityText, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value >= 0 && decimal.Truncate(value * _price.Item.QuantityScale) == value * _price.Item.QuantityScale ? checked((long)(value * _price.Item.QuantityScale)) : throw new InvalidOperationException($"كمية {Name} غير صالحة.");
    public long LineTotalMinor => checked((long)decimal.Round((decimal)UnitPriceMinor * QuantityScaled / QuantityScale, 0, MidpointRounding.AwayFromZero));
    public string LineTotalDisplay => $"{LineTotalMinor / 100m:0.00} ج.م";
}
public sealed class KitchenOrderRow
{
    public KitchenOrderRow(CustomOrderSnapshot order) { Id = order.Id; OrderNumber = order.OrderNumber; CustomerName = order.CustomerName; DueDisplay = order.DueAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"); TotalDisplay = $"{order.TotalMinor / 100m:0.00} ج.م"; StatusDisplay = order.Status.ToString(); }
    public Guid Id { get; } public string OrderNumber { get; } public string CustomerName { get; } public string DueDisplay { get; } public string TotalDisplay { get; } public string StatusDisplay { get; }
}
