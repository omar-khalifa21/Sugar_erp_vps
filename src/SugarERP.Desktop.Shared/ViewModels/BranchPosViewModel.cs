using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using SugarERP.Application;
using SugarERP.Domain;

namespace SugarERP.Desktop.Shared.ViewModels;

public sealed partial class BranchPosViewModel : ViewModelBase
{
    private readonly IBranchOperations _operations;
    private readonly IBranchPrinter _printer;
    private readonly IEnrollmentClient _enrollmentClient;
    private readonly IBranchSyncService _syncService;
    private readonly string _appVersion;
    private readonly AsyncRelayCommand _openShiftCommand;
    private readonly AsyncRelayCommand _startMorningShiftCommand;
    private readonly AsyncRelayCommand _startEveningShiftCommand;
    private readonly AsyncRelayCommand _completeSaleCommand;
    private readonly AsyncRelayCommand _syncCommand;
    private readonly AsyncRelayCommand _enrollCommand;
    private IReadOnlyList<CatalogItemSnapshot> _allItems = [];
    private Guid _pendingSaleCommandId = Guid.NewGuid();
    private Guid? _lastReceiptId;
    private string _searchText = string.Empty;
    private string _openingCashText = "0";
    private string _discountText = "0";
    private string _tipText = "0";
    private string _apiBaseUrl = DeploymentConfiguration.Create(DesktopApplicationType.BranchType1).ApiBaseUrl.ToString().TrimEnd('/');
    private string _enrollmentToken = string.Empty;
    private string _deviceName = Environment.MachineName;
    private string _siteName = "فرع نوع ١";
    private string _shiftLabel = "لا توجد وردية مفتوحة";
    private string _statusMessage = "جارٍ تجهيز البرنامج…";
    private string _pendingSyncText = "جارٍ التحقق من الإرسال";
    private string _receiptNumber = string.Empty;
    private string _receiptTotalText = string.Empty;
    private string _receiptDetails = string.Empty;
    private bool _isBusy;
    private bool _needsEnrollment = true;
    private bool _isShiftOpen;
    private bool _hasReceipt;
    private bool _touchMode = true;
    private bool _isMorningShift = true;
    private bool _isCash = true;
    private bool _isTakeaway = true;
    private bool _isSubmittingSale;
    private BranchPage _currentPage = BranchPage.Menu;

    public BranchPosViewModel(
        IBranchOperations operations,
        IBranchModuleOperations moduleOperations,
        IShiftReportWriter shiftReportWriter,
        IBranchPrinter printer,
        IEnrollmentClient enrollmentClient,
        IBranchSyncService syncService,
        bool isDemo,
        string? appVersion = null)
    {
        _operations = operations;
        _printer = printer;
        _moduleOperations = moduleOperations;
        _shiftReportWriter = shiftReportWriter;
        _enrollmentClient = enrollmentClient;
        _syncService = syncService;
        IsDemo = isDemo;
        _appVersion = string.IsNullOrWhiteSpace(appVersion) ? "1.0.0" : appVersion;

        _openShiftCommand = new AsyncRelayCommand(OpenShiftAsync, CanOpenShift);
        _startMorningShiftCommand = new AsyncRelayCommand(
            () => OpenShiftAsync(ShiftKind.Morning),
            CanOpenShift);
        _startEveningShiftCommand = new AsyncRelayCommand(
            () => OpenShiftAsync(ShiftKind.Evening),
            CanOpenShift);
        _completeSaleCommand = new AsyncRelayCommand(CompleteSaleAsync, CanCompleteSale);
        _syncCommand = new AsyncRelayCommand(SyncAsync, () => IsEnrolled && !IsBusy);
        _enrollCommand = new AsyncRelayCommand(EnrollAsync, () => NeedsEnrollment && !IsBusy);
        DismissReceiptCommand = new RelayCommand(DismissReceipt);
        ReturnToMainCommand = new RelayCommand(() => CurrentPage = BranchPage.Menu);
        ResumeShiftCommand = new RelayCommand(() => CurrentPage = BranchPage.Pos);
        InitializeModuleNavigation();
    }

    public ObservableCollection<ProductTileViewModel> Products { get; } = [];
    public ObservableCollection<CartLineViewModel> Cart { get; } = [];

    public bool IsDemo { get; }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            NotifyCommandState();
        }
    }

    public bool NeedsEnrollment
    {
        get => _needsEnrollment;
        private set
        {
            if (!SetProperty(ref _needsEnrollment, value)) return;
            OnPropertyChanged(nameof(IsEnrolled));
            OnPropertyChanged(nameof(NeedsShift));
            NotifyPageState();
            NotifyCommandState();
        }
    }

    public bool IsEnrolled => !NeedsEnrollment;
    public bool IsShiftOpen
    {
        get => _isShiftOpen;
        private set
        {
            if (!SetProperty(ref _isShiftOpen, value)) return;
            OnPropertyChanged(nameof(NeedsShift));
            NotifyPageState();
            NotifyCommandState();
        }
    }

    public bool NeedsShift => IsEnrolled && !IsShiftOpen;
    public bool IsMenuVisible => CurrentPage == BranchPage.Menu;
    public bool IsPosVisible => IsEnrolled && IsShiftOpen && CurrentPage == BranchPage.Pos;
    public bool CanReturnToMain => CurrentPage != BranchPage.Menu;
    public bool CanResumeShift => IsEnrolled && IsShiftOpen && CurrentPage == BranchPage.Menu;

    public bool ShowMenu
    {
        get => CurrentPage == BranchPage.Menu;
        private set
        {
            CurrentPage = value ? BranchPage.Menu : BranchPage.Pos;
        }
    }

    private BranchPage CurrentPage
    {
        get => _currentPage;
        set
        {
            if (!SetProperty(ref _currentPage, value)) return;
            NotifyPageState();
        }
    }

    public bool HasReceipt
    {
        get => _hasReceipt;
        private set => SetProperty(ref _hasReceipt, value);
    }

    public string SiteName
    {
        get => _siteName;
        private set => SetProperty(ref _siteName, value);
    }

    public string ShiftLabel
    {
        get => _shiftLabel;
        private set => SetProperty(ref _shiftLabel, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            ApplyProductFilter();
        }
    }

    public string OpeningCashText
    {
        get => _openingCashText;
        set
        {
            if (!SetProperty(ref _openingCashText, value)) return;
            _openShiftCommand.NotifyCanExecuteChanged();
            _startMorningShiftCommand.NotifyCanExecuteChanged();
            _startEveningShiftCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsMorningShift
    {
        get => _isMorningShift;
        set
        {
            if (!value || !SetProperty(ref _isMorningShift, true)) return;
            OnPropertyChanged(nameof(IsEveningShift));
        }
    }

    public bool IsEveningShift
    {
        get => !_isMorningShift;
        set
        {
            if (!value || !SetProperty(ref _isMorningShift, false, nameof(IsMorningShift))) return;
            OnPropertyChanged();
        }
    }

    public bool IsCash
    {
        get => _isCash;
        set
        {
            if (!value || !SetProperty(ref _isCash, true)) return;
            OnPropertyChanged(nameof(IsVisa));
        }
    }

    public bool IsVisa
    {
        get => !_isCash;
        set
        {
            if (!value || !SetProperty(ref _isCash, false, nameof(IsCash))) return;
            OnPropertyChanged();
        }
    }

    public bool IsTakeaway
    {
        get => _isTakeaway;
        set
        {
            if (!value || !SetProperty(ref _isTakeaway, true)) return;
            OnPropertyChanged(nameof(IsTable));
        }
    }

    public bool IsTable
    {
        get => !_isTakeaway;
        set
        {
            if (!value || !SetProperty(ref _isTakeaway, false, nameof(IsTakeaway))) return;
            OnPropertyChanged();
        }
    }

    public string DiscountText
    {
        get => _discountText;
        set
        {
            if (!SetProperty(ref _discountText, value)) return;
            OnTotalsChanged();
        }
    }

    public string TipText
    {
        get => _tipText;
        set
        {
            if (!SetProperty(ref _tipText, value)) return;
            OnTotalsChanged();
        }
    }

    public string SubtotalText => ArabicDisplay.Money(Cart.Sum(value => value.LineTotalMinor));
    public string TotalText
    {
        get
        {
            var subtotal = Cart.Sum(value => value.LineTotalMinor);
            return TryGetTotals(out var discount, out var tip, out _) ? ArabicDisplay.Money(subtotal - discount + tip) : "—";
        }
    }

    public string ValidationMessage
    {
        get
        {
            _ = TryGetTotals(out _, out _, out var message);
            return message;
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string PendingSyncText
    {
        get => _pendingSyncText;
        private set => SetProperty(ref _pendingSyncText, value);
    }

    public string ReceiptNumber
    {
        get => _receiptNumber;
        private set => SetProperty(ref _receiptNumber, value);
    }

    public string ReceiptTotalText
    {
        get => _receiptTotalText;
        private set => SetProperty(ref _receiptTotalText, value);
    }

    public string ReceiptDetails
    {
        get => _receiptDetails;
        private set => SetProperty(ref _receiptDetails, value);
    }

    public string ApiBaseUrl
    {
        get => _apiBaseUrl;
        set => SetProperty(ref _apiBaseUrl, value);
    }

    public string EnrollmentToken
    {
        get => _enrollmentToken;
        set => SetProperty(ref _enrollmentToken, value);
    }

    public string DeviceName
    {
        get => _deviceName;
        set => SetProperty(ref _deviceName, value);
    }

    public bool TouchMode
    {
        get => _touchMode;
        set => SetProperty(ref _touchMode, value);
    }

    public ICommand OpenShiftCommand => _openShiftCommand;
    public ICommand StartMorningShiftCommand => _startMorningShiftCommand;
    public ICommand StartEveningShiftCommand => _startEveningShiftCommand;
    public ICommand CompleteSaleCommand => _completeSaleCommand;
    public ICommand SyncCommand => _syncCommand;
    public ICommand EnrollCommand => _enrollCommand;
    public ICommand DismissReceiptCommand { get; }
    public ICommand ReturnToMainCommand { get; }
    public ICommand ResumeShiftCommand { get; }

    public async Task InitializeAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await _operations.InitializeAsync();
            if (IsDemo) await _operations.SeedSyntheticDemoAsync();
            await RefreshSnapshotAsync();
            await InitializeModuleDataAsync();
            StatusMessage = IsDemo
                ? "وضع تجريبي"
                : NeedsEnrollment
                    ? "اربط الجهاز بالخادم من الإعدادات لإرسال البيانات والبدء في العمل."
                    : "تم تحميل البيانات المحلية بأمان.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"تعذر بدء البرنامج: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanOpenShift() => NeedsShift && !IsBusy && ArabicDisplay.TryParseMoney(OpeningCashText, out _);

    private async Task OpenShiftAsync()
    {
        await OpenShiftAsync(IsMorningShift ? ShiftKind.Morning : ShiftKind.Evening);
    }

    private async Task OpenShiftAsync(ShiftKind kind)
    {
        if (!ArabicDisplay.TryParseMoney(OpeningCashText, out var openingCash))
        {
            StatusMessage = "أدخل قيمة صحيحة للعهدة الافتتاحية.";
            return;
        }

        IsBusy = true;
        try
        {
            IsMorningShift = kind == ShiftKind.Morning;
            IsEveningShift = kind == ShiftKind.Evening;
            await _operations.OpenShiftAsync(kind, openingCash);
            ShowMenu = false;
            await RefreshSnapshotAsync();
            StatusMessage = "تم فتح الوردية. يمكنك بدء البيع الآن.";
        }
        catch (BusinessRuleException exception)
        {
            StatusMessage = exception.UserMessage;
        }
        catch (Exception)
        {
            StatusMessage = "تعذر فتح الوردية الآن. لم يتم تغيير المخزون.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanCompleteSale() => IsShiftOpen && Cart.Count > 0 && !IsBusy && TryGetTotals(out _, out _, out _);

    private async Task CompleteSaleAsync()
    {
        if (_isSubmittingSale) return;
        if (!TryGetTotals(out var discount, out var tip, out var validation))
        {
            if (!string.IsNullOrEmpty(validation)) StatusMessage = validation;
            return;
        }

        _isSubmittingSale = true;
        IsBusy = true;
        var commandId = _pendingSaleCommandId;
        try
        {
            var command = new CompleteSaleCommand(
                commandId,
                Cart.Select(value => new SaleCartLine(value.ItemId, value.QuantityScaled)).ToArray(),
                IsCash ? PaymentMethod.Cash : PaymentMethod.Visa,
                IsTakeaway ? FulfillmentKind.Takeaway : FulfillmentKind.Table,
                discount,
                tip);
            var receipt = await _operations.CompleteSaleAsync(command);
            _lastReceiptId = receipt.Id;
            _reprintLastReceiptCommand.NotifyCanExecuteChanged();
            ReceiptNumber = receipt.ReceiptNumber;
            ReceiptTotalText = ArabicDisplay.Money(receipt.TotalMinor);
            ReceiptDetails = $"{(receipt.PaymentMethod == PaymentMethod.Cash ? "نقدي" : "فيزا")} · {receipt.OccurredAtUtc.ToLocalTime():HH:mm}";
            HasReceipt = true;
            Cart.Clear();
            DiscountText = "0";
            TipText = "0";
            _pendingSaleCommandId = Guid.NewGuid();
            await RefreshSnapshotAsync();
            var savedMessage = receipt.WasAlreadyCommitted
                ? "هذا البيع كان محفوظاً بالفعل؛ عُرض الإيصال دون تكراره."
                : "تم حفظ البيع.";
            StatusMessage = savedMessage;
            if (!receipt.WasAlreadyCommitted)
                await TryPrintSaleAsync(receipt.Id, savedMessage);
        }
        catch (BusinessRuleException exception)
        {
            StatusMessage = exception.UserMessage;
        }
        catch (Exception)
        {
            StatusMessage = "تعذر حفظ البيع. راجع الحالة ثم حاول مرة أخرى؛ زر الدفع محمي من التكرار.";
        }
        finally
        {
            _isSubmittingSale = false;
            IsBusy = false;
            OnCartChanged();
        }
    }

    private async Task SyncAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _syncService.SynchronizeAsync();
            StatusMessage = result.UserMessage;
            await RefreshSnapshotAsync();
        }
        catch (Exception)
        {
            StatusMessage = "تعذر الإرسال الآن. كل الحركات محفوظة وستُرسل لاحقاً.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task EnrollAsync()
    {
        if (!TryValidateEnrollmentUrl(ApiBaseUrl, out var uri))
        {
            StatusMessage = "تحقق من عنوان الاتصال.";
            return;
        }
        if (string.IsNullOrWhiteSpace(EnrollmentToken) || string.IsNullOrWhiteSpace(DeviceName))
        {
            StatusMessage = "أدخل رمز التسجيل واسم الجهاز.";
            return;
        }

        IsBusy = true;
        try
        {
            var keyMaterial = RandomNumberGenerator.GetBytes(32);
            var thumbprint = Convert.ToHexStringLower(SHA256.HashData(keyMaterial));
            var command = new EnrollmentCommand(uri, EnrollmentToken.Trim(), DeviceName.Trim(), thumbprint, _appVersion, TouchMode, DeviceProfile.BranchType1);
            var result = await _enrollmentClient.EnrollAsync(command);
            await _operations.SaveEnrollmentAsync(command, result);
            EnrollmentToken = string.Empty;
            await RefreshSnapshotAsync();
            StatusMessage = "تم تسجيل الجهاز بنجاح.";
        }
        catch (BusinessRuleException exception)
        {
            StatusMessage = exception.UserMessage;
        }
        catch (Exception)
        {
            StatusMessage = "تعذر تسجيل الجهاز. تحقق من الإنترنت والعنوان ثم حاول مرة أخرى.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshSnapshotAsync()
    {
        var snapshot = await _operations.GetSnapshotAsync();
        NeedsEnrollment = snapshot.Configuration is null;
        SiteName = snapshot.Configuration?.SiteName ?? "فرع نوع ١";
        if (snapshot.Configuration is not null)
            ApiBaseUrl = snapshot.Configuration.ApiBaseUrl;
        IsShiftOpen = snapshot.OpenShift is not null;
        ShiftLabel = snapshot.OpenShift is null
            ? "لا توجد وردية مفتوحة"
            : $"وردية {(snapshot.OpenShift.Kind == ShiftKind.Morning ? "صباحية" : "مسائية")} · {snapshot.OpenShift.BusinessDate} · {snapshot.OpenShift.ReceiptCount} إيصال · {ArabicDisplay.Money(snapshot.OpenShift.SalesMinor)}";
        PendingSyncText = snapshot.PendingOutboxCount == 0
            ? "تم إرسال كل التغييرات"
            : $"{snapshot.PendingOutboxCount} تغيير بانتظار الاتصال";
        _allItems = snapshot.Items;
        ApplyProductFilter();
    }

    private void ApplyProductFilter()
    {
        var query = SearchText.Trim();
        var matches = string.IsNullOrWhiteSpace(query)
            ? _allItems
            : _allItems.Where(value =>
                value.NameAr.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || value.Sku.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        Products.Clear();
        foreach (var item in matches)
            Products.Add(new ProductTileViewModel(item, AddProduct));
    }

    private void AddProduct(ProductTileViewModel product)
    {
        if (!IsShiftOpen)
        {
            StatusMessage = "افتح الوردية قبل إضافة أصناف للبيع.";
            return;
        }

        var existing = Cart.SingleOrDefault(value => value.ItemId == product.Id);
        if (existing is not null)
        {
            if (existing.IncreaseCommand.CanExecute(null)) existing.IncreaseCommand.Execute(null);
            else StatusMessage = "وصلت إلى كامل الكمية المتاحة من هذا الصنف.";
            return;
        }

        Cart.Add(new CartLineViewModel(product.Item, line => Cart.Remove(line), OnCartChanged));
        OnCartChanged();
    }

    private void OnCartChanged()
    {
        if (!_isSubmittingSale) _pendingSaleCommandId = Guid.NewGuid();
        OnPropertyChanged(nameof(SubtotalText));
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(ValidationMessage));
        _completeSaleCommand.NotifyCanExecuteChanged();
    }

    private void OnTotalsChanged()
    {
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(ValidationMessage));
        _pendingSaleCommandId = Guid.NewGuid();
        _completeSaleCommand.NotifyCanExecuteChanged();
    }

    private bool TryGetTotals(out long discount, out long tip, out string validation)
    {
        discount = 0;
        tip = 0;
        validation = string.Empty;
        if (!ArabicDisplay.TryParseMoney(DiscountText, out discount))
        {
            validation = "قيمة الخصم غير صحيحة.";
            return false;
        }
        if (!ArabicDisplay.TryParseMoney(TipText, out tip))
        {
            validation = "قيمة الإكرامية غير صحيحة.";
            return false;
        }
        if (discount > Cart.Sum(value => value.LineTotalMinor))
        {
            validation = "الخصم لا يمكن أن يتجاوز إجمالي الأصناف.";
            return false;
        }
        return true;
    }

    private void DismissReceipt() => HasReceipt = false;

    private void NotifyCommandState()
    {
        _openShiftCommand.NotifyCanExecuteChanged();
        _startMorningShiftCommand.NotifyCanExecuteChanged();
        _startEveningShiftCommand.NotifyCanExecuteChanged();
        _completeSaleCommand.NotifyCanExecuteChanged();
        _syncCommand.NotifyCanExecuteChanged();
        _enrollCommand.NotifyCanExecuteChanged();
        NotifyModuleCommandState();
    }

    private void NotifyPageState()
    {
        OnPropertyChanged(nameof(IsMenuVisible));
        OnPropertyChanged(nameof(IsPosVisible));
        OnPropertyChanged(nameof(IsCatalogVisible));
        OnPropertyChanged(nameof(IsCustomOrdersVisible));
        OnPropertyChanged(nameof(IsRequestVisible));
        OnPropertyChanged(nameof(IsIncomingVisible));
        OnPropertyChanged(nameof(IsShiftHistoryVisible));
        OnPropertyChanged(nameof(IsReturnVisible));
        OnPropertyChanged(nameof(IsCloseVisible));
        OnPropertyChanged(nameof(IsHistoryVisible));
        OnPropertyChanged(nameof(IsSettingsVisible));
        OnPropertyChanged(nameof(CurrentPageTitle));
        OnPropertyChanged(nameof(CanReturnToMain));
        OnPropertyChanged(nameof(CanResumeShift));
    }

    private static bool TryValidateEnrollmentUrl(string value, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(value.Trim().TrimEnd('/'), UriKind.Absolute, out var parsed)) return false;
        var secure = parsed.Scheme == Uri.UriSchemeHttps;
        var localDevelopment = parsed.Scheme == Uri.UriSchemeHttp && parsed.IsLoopback;
        if (!secure && !localDevelopment) return false;
        if (!parsed.AbsolutePath.TrimEnd('/').EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase)) return false;
        uri = parsed;
        return true;
    }
}
