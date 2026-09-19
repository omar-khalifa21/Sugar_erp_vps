using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using SugarERP.Application;
using SugarERP.Domain;

namespace SugarERP.Desktop.Shared.ViewModels;

public sealed partial class BranchPosViewModel
{
    private CafeProfileRowViewModel? _selectedCafe;
    private CafeProfileRowViewModel? _cafeCopySource;
    private CustomOrderRowViewModel? _selectedCustomOrder;
    private string _newCafeName = string.Empty;
    private string _newCafePhone = string.Empty;
    private string _newCafeAddress = string.Empty;
    private bool _newCustomerIsCafe = true;
    private string _customOrderDescription = string.Empty;
    private string _customOrderDueText = DateTime.Now.AddDays(2).ToString("yyyy-MM-dd 12:00", CultureInfo.InvariantCulture);
    private string _customOrderPaymentText = "0";
    private string _customOrderStatusText = "اختر كافيه أو سوبر ماركت لعرض حسابه.";
    private bool _isCreatingCafe;
    private bool _isEditingCafePrices;
    private bool _isCreatingCustomOrder;
    private bool _isCustomOrderPaymentCash = true;
    private AsyncRelayCommand _createCafeCommand = null!;
    private AsyncRelayCommand _saveCafePricesCommand = null!;
    private AsyncRelayCommand _createCustomOrderCommand = null!;
    private AsyncRelayCommand _addCustomOrderPaymentCommand = null!;
    private AsyncRelayCommand _confirmCustomOrderCommand = null!;
    private AsyncRelayCommand _readyCustomOrderCommand = null!;
    private AsyncRelayCommand _deliverCustomOrderCommand = null!;
    private AsyncRelayCommand _cancelCustomOrderCommand = null!;
    private AsyncRelayCommand _printCustomOrderCommand = null!;
    private RelayCommand _newCafeCommand = null!;
    private RelayCommand _backToCafeProfilesCommand = null!;
    private RelayCommand _newCustomOrderCommand = null!;
    private RelayCommand _editCafePricesCommand = null!;
    private RelayCommand _cancelCafeActionCommand = null!;

    public ObservableCollection<CafeProfileRowViewModel> CafeProfiles { get; } = [];
    public ObservableCollection<CafePriceRowViewModel> CafePriceRows { get; } = [];
    public ObservableCollection<CafePaymentRowViewModel> CafePaymentRows { get; } = [];
    public ObservableCollection<CustomOrderRowViewModel> CustomOrderRows { get; } = [];

    public CafeProfileRowViewModel? SelectedCafe
    {
        get => _selectedCafe;
        set
        {
            if (!SetProperty(ref _selectedCafe, value)) return;
            SelectedCustomOrder = null;
            IsCreatingCafe = false;
            IsCreatingCustomOrder = false;
            IsEditingCafePrices = false;
            NotifyCafeVisibility();
            NotifyCustomOrderCommandState();
            if (value is not null) _ = LoadCafeDetailsSafeAsync(value.Id);
        }
    }
    public CafeProfileRowViewModel? CafeCopySource { get => _cafeCopySource; set => SetProperty(ref _cafeCopySource, value); }
    public CustomOrderRowViewModel? SelectedCustomOrder
    {
        get => _selectedCustomOrder;
        set
        {
            if (!SetProperty(ref _selectedCustomOrder, value)) return;
            if (value is not null) CustomOrderPaymentText = (Math.Max(0, value.Snapshot.CustomerBalanceMinor) / 100m).ToString("0.00", CultureInfo.InvariantCulture);
            OnPropertyChanged(nameof(HasSelectedCustomOrder));
            OnPropertyChanged(nameof(CanConfirmCustomOrder));
            OnPropertyChanged(nameof(CanReadyCustomOrder));
            OnPropertyChanged(nameof(CanDeliverCustomOrder));
            OnPropertyChanged(nameof(CanCancelCustomOrder));
            NotifyCustomOrderCommandState();
        }
    }

    public string NewCafeName { get => _newCafeName; set => SetProperty(ref _newCafeName, value); }
    public string NewCafePhone { get => _newCafePhone; set => SetProperty(ref _newCafePhone, value); }
    public string NewCafeAddress { get => _newCafeAddress; set => SetProperty(ref _newCafeAddress, value); }
    public bool NewCustomerIsCafe
    {
        get => _newCustomerIsCafe;
        set { if (value && SetProperty(ref _newCustomerIsCafe, true)) OnPropertyChanged(nameof(NewCustomerIsSupermarket)); }
    }
    public bool NewCustomerIsSupermarket
    {
        get => !_newCustomerIsCafe;
        set { if (value && SetProperty(ref _newCustomerIsCafe, false, nameof(NewCustomerIsCafe))) OnPropertyChanged(); }
    }
    public string CustomOrderDescription { get => _customOrderDescription; set => SetProperty(ref _customOrderDescription, value); }
    public string CustomOrderDueText { get => _customOrderDueText; set => SetProperty(ref _customOrderDueText, value); }
    public string CustomOrderPaymentText { get => _customOrderPaymentText; set => SetProperty(ref _customOrderPaymentText, value); }
    public string CustomOrderStatusText { get => _customOrderStatusText; private set => SetProperty(ref _customOrderStatusText, value); }
    public bool IsCreatingCafe { get => _isCreatingCafe; private set { if (SetProperty(ref _isCreatingCafe, value)) NotifyCafeVisibility(); } }
    public bool IsEditingCafePrices { get => _isEditingCafePrices; private set { if (SetProperty(ref _isEditingCafePrices, value)) NotifyCafeVisibility(); } }
    public bool IsCreatingCustomOrder { get => _isCreatingCustomOrder; private set { if (SetProperty(ref _isCreatingCustomOrder, value)) NotifyCafeVisibility(); } }
    public bool IsCafeProfileListVisible => SelectedCafe is null && !IsCreatingCafe;
    public bool HasSelectedCafe => SelectedCafe is not null && !IsCreatingCafe;
    public bool IsCafeOverviewVisible => HasSelectedCafe && !IsCreatingCustomOrder && !IsEditingCafePrices;
    public bool HasSelectedCustomOrder => SelectedCustomOrder is not null;
    public string CafeBalanceText => SelectedCafe?.BalanceText ?? ArabicDisplay.Money(0);
    public bool IsCustomOrderPaymentCash
    {
        get => _isCustomOrderPaymentCash;
        set { if (value && SetProperty(ref _isCustomOrderPaymentCash, true)) OnPropertyChanged(nameof(IsCustomOrderPaymentVisa)); }
    }
    public bool IsCustomOrderPaymentVisa
    {
        get => !_isCustomOrderPaymentCash;
        set { if (value && SetProperty(ref _isCustomOrderPaymentCash, false, nameof(IsCustomOrderPaymentCash))) OnPropertyChanged(); }
    }
    public bool CanConfirmCustomOrder => false;
    public bool CanReadyCustomOrder => false;
    public bool CanDeliverCustomOrder => SelectedCustomOrder?.Snapshot.Status is CustomOrderStatus.New or CustomOrderStatus.Confirmed or CustomOrderStatus.Ready;
    public bool CanCancelCustomOrder => SelectedCustomOrder?.Snapshot.Status is CustomOrderStatus.New or CustomOrderStatus.Confirmed or CustomOrderStatus.Ready;

    public ICommand NewCafeCommand => _newCafeCommand;
    public ICommand CreateCafeCommand => _createCafeCommand;
    public ICommand BackToCafeProfilesCommand => _backToCafeProfilesCommand;
    public ICommand EditCafePricesCommand => _editCafePricesCommand;
    public ICommand SaveCafePricesCommand => _saveCafePricesCommand;
    public ICommand CancelCafeActionCommand => _cancelCafeActionCommand;
    public ICommand NewCustomOrderCommand => _newCustomOrderCommand;
    public ICommand CreateCustomOrderCommand => _createCustomOrderCommand;
    public ICommand AddCustomOrderPaymentCommand => _addCustomOrderPaymentCommand;
    public ICommand ConfirmCustomOrderCommand => _confirmCustomOrderCommand;
    public ICommand ReadyCustomOrderCommand => _readyCustomOrderCommand;
    public ICommand DeliverCustomOrderCommand => _deliverCustomOrderCommand;
    public ICommand CancelCustomOrderCommand => _cancelCustomOrderCommand;
    public ICommand PrintCustomOrderCommand => _printCustomOrderCommand;

    private void InitializeCustomOrderCommands()
    {
        _newCafeCommand = new RelayCommand(NewCafe);
        _createCafeCommand = new AsyncRelayCommand(CreateCafeAsync, () => !IsBusy);
        _backToCafeProfilesCommand = new RelayCommand(() =>
        {
            IsCreatingCafe = false;
            IsEditingCafePrices = false;
            IsCreatingCustomOrder = false;
            SelectedCafe = null;
            NotifyCafeVisibility();
        });
        _editCafePricesCommand = new RelayCommand(() => IsEditingCafePrices = true, () => SelectedCafe is not null);
        _saveCafePricesCommand = new AsyncRelayCommand(SaveCafePricesAsync, () => SelectedCafe is not null && !IsBusy);
        _cancelCafeActionCommand = new RelayCommand(() => { IsEditingCafePrices = false; IsCreatingCustomOrder = false; });
        _newCustomOrderCommand = new RelayCommand(NewCustomOrder, () => SelectedCafe is not null);
        _createCustomOrderCommand = new AsyncRelayCommand(CreateCustomOrderAsync, () => SelectedCafe is not null && !IsBusy);
        _addCustomOrderPaymentCommand = new AsyncRelayCommand(AddCustomOrderPaymentAsync, () => SelectedCustomOrder is not null && SelectedCafe?.Snapshot.BalanceMinor > 0 && !IsBusy);
        _confirmCustomOrderCommand = new AsyncRelayCommand(() => ChangeCustomOrderStatusAsync(CustomOrderStatus.Confirmed), () => CanConfirmCustomOrder && !IsBusy);
        _readyCustomOrderCommand = new AsyncRelayCommand(() => ChangeCustomOrderStatusAsync(CustomOrderStatus.Ready), () => CanReadyCustomOrder && !IsBusy);
        _deliverCustomOrderCommand = new AsyncRelayCommand(() => ChangeCustomOrderStatusAsync(CustomOrderStatus.Delivered), () => CanDeliverCustomOrder && !IsBusy);
        _cancelCustomOrderCommand = new AsyncRelayCommand(() => ChangeCustomOrderStatusAsync(CustomOrderStatus.Cancelled), () => CanCancelCustomOrder && !IsBusy);
        _printCustomOrderCommand = new AsyncRelayCommand(PrintSelectedCustomOrderAsync, () => SelectedCustomOrder is not null && !IsBusy);
    }

    private void NotifyCafeVisibility()
    {
        OnPropertyChanged(nameof(IsCafeProfileListVisible));
        OnPropertyChanged(nameof(HasSelectedCafe));
        OnPropertyChanged(nameof(IsCafeOverviewVisible));
        OnPropertyChanged(nameof(CafeBalanceText));
    }

    private void NotifyCustomOrderCommandState()
    {
        if (_createCustomOrderCommand is null) return;
        _createCafeCommand.NotifyCanExecuteChanged();
        _newCustomOrderCommand.NotifyCanExecuteChanged();
        _editCafePricesCommand.NotifyCanExecuteChanged();
        _saveCafePricesCommand.NotifyCanExecuteChanged();
        _createCustomOrderCommand.NotifyCanExecuteChanged();
        _addCustomOrderPaymentCommand.NotifyCanExecuteChanged();
        _confirmCustomOrderCommand.NotifyCanExecuteChanged();
        _readyCustomOrderCommand.NotifyCanExecuteChanged();
        _deliverCustomOrderCommand.NotifyCanExecuteChanged();
        _cancelCustomOrderCommand.NotifyCanExecuteChanged();
        _printCustomOrderCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadCustomOrdersAsync()
    {
        var selectedId = SelectedCafe?.Id;
        var profiles = await _moduleOperations.GetCafeProfilesAsync();
        CafeProfiles.Clear();
        foreach (var profile in profiles) CafeProfiles.Add(new CafeProfileRowViewModel(profile));
        if (selectedId is Guid id)
        {
            _selectedCafe = CafeProfiles.FirstOrDefault(value => value.Id == id);
            OnPropertyChanged(nameof(SelectedCafe));
            if (_selectedCafe is not null) await LoadCafeDetailsAsync(id);
        }
        CustomOrderStatusText = profiles.Count == 0 ? "أضف أول كافيه أو سوبر ماركت." : $"{profiles.Count} حساب محفوظ";
        NotifyCafeVisibility();
    }

    private async Task LoadCafeDetailsSafeAsync(Guid cafeId)
    {
        try { await LoadCafeDetailsAsync(cafeId); }
        catch (BusinessRuleException exception) { CustomOrderStatusText = exception.UserMessage; }
        catch { CustomOrderStatusText = "تعذر فتح حساب العميل."; }
    }

    private async Task LoadCafeDetailsAsync(Guid cafeId)
    {
        var selectedOrderId = SelectedCustomOrder?.Id;
        var details = await _moduleOperations.GetCafeProfileAsync(cafeId);
        var profileRow = CafeProfiles.FirstOrDefault(value => value.Id == cafeId) ?? new CafeProfileRowViewModel(details.Profile);
        profileRow.Update(details.Profile);
        _selectedCafe = profileRow;
        CafePriceRows.Clear();
        foreach (var price in details.Prices) CafePriceRows.Add(new CafePriceRowViewModel(price));
        CustomOrderRows.Clear();
        foreach (var order in details.Orders) CustomOrderRows.Add(new CustomOrderRowViewModel(order));
        CafePaymentRows.Clear();
        foreach (var payment in details.Payments) CafePaymentRows.Add(new CafePaymentRowViewModel(payment));
        SelectedCustomOrder = selectedOrderId is null ? null : CustomOrderRows.FirstOrDefault(value => value.Id == selectedOrderId);
        CustomOrderStatusText = $"{details.Orders.Count} فاتورة · رصيد العميل {ArabicDisplay.Money(details.Profile.BalanceMinor)}";
        NotifyCafeVisibility();
        NotifyCustomOrderCommandState();
    }

    private void NewCafe()
    {
        _selectedCafe = null;
        OnPropertyChanged(nameof(SelectedCafe));
        NewCafeName = string.Empty;
        NewCafePhone = string.Empty;
        NewCafeAddress = string.Empty;
        NewCustomerIsCafe = true;
        CafeCopySource = null;
        IsCreatingCafe = true;
        CustomOrderStatusText = "أنشئ الحساب، ويمكنك نسخ أسعار عميل محفوظ ثم تعديلها وحدها.";
    }

    private async Task CreateCafeAsync()
    {
        IsBusy = true;
        try
        {
            var copyId = CafeCopySource?.Id;
            var created = await _moduleOperations.CreateCafeProfileAsync(new CreateCafeProfileCommand(
                Guid.NewGuid(), NewCafeName, NewCustomerIsCafe ? "كافيه" : "سوبر ماركت", NewCafePhone, NewCafeAddress, copyId));
            IsCreatingCafe = false;
            await LoadCustomOrdersAsync();
            SelectedCafe = CafeProfiles.FirstOrDefault(value => value.Id == created.Id);
            CustomOrderStatusText = copyId is null ? "تم إنشاء الحساب بأسعار الأصناف الحالية." : "تم إنشاء الحساب ونسخ قائمة الأسعار. يمكنك تعديلها دون تغيير العميل الأصلي.";
        }
        catch (BusinessRuleException exception) { CustomOrderStatusText = exception.UserMessage; }
        catch { CustomOrderStatusText = "تعذر إنشاء الحساب. لم يتغير شيء."; }
        finally { IsBusy = false; }
    }

    private async Task SaveCafePricesAsync()
    {
        if (SelectedCafe is null) return;
        var prices = new List<SaveCafePriceInput>();
        foreach (var row in CafePriceRows)
        {
            if (!ArabicDisplay.TryParseMoney(row.PriceText, out var price) || price < 0) { CustomOrderStatusText = $"راجع سعر {row.Name}."; return; }
            prices.Add(new SaveCafePriceInput(row.ItemId, price));
        }
        IsBusy = true;
        try
        {
            var cafeId = SelectedCafe.Id;
            await _moduleOperations.SaveCafePriceListAsync(new SaveCafePriceListCommand(Guid.NewGuid(), cafeId, SelectedCafe.Snapshot.Version, prices));
            IsEditingCafePrices = false;
            await LoadCafeDetailsAsync(cafeId);
            CustomOrderStatusText = "تم حفظ قائمة الأسعار لهذا العميل فقط.";
        }
        catch (BusinessRuleException exception) { CustomOrderStatusText = exception.UserMessage; }
        catch { CustomOrderStatusText = "تعذر حفظ الأسعار. لم يتغير شيء."; }
        finally { IsBusy = false; }
    }

    private void NewCustomOrder()
    {
        foreach (var row in CafePriceRows) row.QuantityText = "0";
        CustomOrderDescription = string.Empty;
        CustomOrderDueText = DateTime.Now.AddDays(2).ToString("yyyy-MM-dd 12:00", CultureInfo.InvariantCulture);
        IsCreatingCustomOrder = true;
        IsEditingCafePrices = false;
        CustomOrderStatusText = "اختر الكميات؛ ستُستخدم أسعار هذا العميل وتُحفظ داخل الفاتورة.";
    }

    private async Task CreateCustomOrderAsync()
    {
        if (SelectedCafe is null) return;
        if (!TryParseCustomOrderDue(CustomOrderDueText, out var dueAtUtc)) { CustomOrderStatusText = "اكتب الموعد بهذا الشكل: 2026-09-15 18:30"; return; }
        var lines = new List<CafeOrderLineInput>();
        foreach (var row in CafePriceRows)
        {
            if (!TryParseQuantity(row.QuantityText, row.Snapshot.QuantityScale, out var quantity)) { CustomOrderStatusText = $"راجع كمية {row.Name}."; return; }
            if (quantity > 0) lines.Add(new CafeOrderLineInput(row.ItemId, quantity));
        }
        if (lines.Count == 0) { CustomOrderStatusText = "اختر صنفاً واحداً على الأقل."; return; }
        IsBusy = true;
        try
        {
            var cafeId = SelectedCafe.Id;
            var created = await _moduleOperations.CreateCustomOrderAsync(new CreateCustomOrderCommand(Guid.NewGuid(), cafeId, CustomOrderDescription, dueAtUtc, lines));
            IsCreatingCustomOrder = false;
            await LoadCafeDetailsAsync(cafeId);
            SelectedCustomOrder = CustomOrderRows.FirstOrDefault(value => value.Id == created.Id);
            CustomOrderStatusText = "تم حفظ الطلب وإضافته إلى حساب العميل. ستُطبع الفاتورة عند التسليم.";
        }
        catch (BusinessRuleException exception) { CustomOrderStatusText = exception.UserMessage; }
        catch { CustomOrderStatusText = "تعذر حفظ الطلب. لم يتغير شيء."; }
        finally { IsBusy = false; }
    }

    private async Task AddCustomOrderPaymentAsync()
    {
        if (SelectedCustomOrder is null || SelectedCafe is null) return;
        if (!ArabicDisplay.TryParseMoney(CustomOrderPaymentText, out var payment) || payment <= 0) { CustomOrderStatusText = "أدخل مبلغاً صحيحاً."; return; }
        IsBusy = true;
        try
        {
            var cafeId = SelectedCafe.Id;
            var updated = await _moduleOperations.AddCustomOrderPaymentAsync(new AddCustomOrderPaymentCommand(
                Guid.NewGuid(), SelectedCustomOrder.Id, SelectedCustomOrder.Snapshot.Version, payment,
                IsCustomOrderPaymentCash ? PaymentMethod.Cash : PaymentMethod.Visa));
            await LoadCafeDetailsAsync(cafeId);
            SelectedCustomOrder = CustomOrderRows.FirstOrDefault(value => value.Id == updated.Id);
            CustomOrderStatusText = "تم تسجيل السداد على الحساب، وتوزيعه على أقدم الفواتير أولاً.";
        }
        catch (BusinessRuleException exception) { CustomOrderStatusText = exception.UserMessage; }
        catch { CustomOrderStatusText = "تعذر تسجيل السداد. لم يتغير شيء."; }
        finally { IsBusy = false; }
    }

    private async Task ChangeCustomOrderStatusAsync(CustomOrderStatus status)
    {
        if (SelectedCustomOrder is null || SelectedCafe is null) return;
        IsBusy = true;
        try
        {
            var cafeId = SelectedCafe.Id;
            var updated = await _moduleOperations.ChangeCustomOrderStatusAsync(new ChangeCustomOrderStatusCommand(Guid.NewGuid(), SelectedCustomOrder.Id, SelectedCustomOrder.Snapshot.Version, status));
            await LoadCafeDetailsAsync(cafeId);
            SelectedCustomOrder = CustomOrderRows.FirstOrDefault(value => value.Id == updated.Id);
            if (status == CustomOrderStatus.Confirmed)
                CustomOrderStatusText = await TryPrintCustomOrderAsync(updated, "تم تأكيد الطلب وإصدار الفاتورة.");
            else if (status == CustomOrderStatus.Delivered)
                CustomOrderStatusText = await TryPrintCustomOrderAsync(updated, "تم تسليم الطلب وإصدار الفاتورة.");
            else
                CustomOrderStatusText = status switch { CustomOrderStatus.Ready => "تم تعليم الطلب كجاهز.", _ => "تم إلغاء الطلب وتحديث حساب العميل." };
        }
        catch (BusinessRuleException exception) { CustomOrderStatusText = exception.UserMessage; }
        catch { CustomOrderStatusText = "تعذر تحديث الطلب. لم يتغير شيء."; }
        finally { IsBusy = false; }
    }

    private async Task PrintSelectedCustomOrderAsync()
    {
        if (SelectedCustomOrder is null) return;
        IsBusy = true;
        try { CustomOrderStatusText = await TryPrintCustomOrderAsync(SelectedCustomOrder.Snapshot, "الفاتورة محفوظة."); }
        finally { IsBusy = false; }
    }

    private async Task<string> TryPrintCustomOrderAsync(CustomOrderSnapshot order, string prefix)
    {
        if (InstalledPrinterNames.Count == 0 && string.IsNullOrWhiteSpace(PrinterName)) return $"{prefix} يمكن طباعتها عند توفر طابعة.";
        Guid jobId = Guid.Empty;
        try
        {
            jobId = await _moduleOperations.GetOrResetPrintJobAsync(order.Id, SideEffectKind.PrintCustomOrder);
            await _printer.PrintCustomOrderAsync(PrinterName, SiteName, order);
            await _moduleOperations.MarkPrintSucceededAsync(jobId);
            return $"{prefix} تمت طباعة الفاتورة.";
        }
        catch (Exception exception)
        {
            try
            {
                if (jobId == Guid.Empty) jobId = await _moduleOperations.GetOrResetPrintJobAsync(order.Id, SideEffectKind.PrintCustomOrder);
                await _moduleOperations.MarkPrintFailedAsync(jobId, exception.Message);
            }
            catch { }
            return $"{prefix} تعذرت الطباعة ويمكن إعادتها لاحقاً.";
        }
    }

    private static bool TryParseCustomOrderDue(string value, out DateTimeOffset dueAtUtc)
    {
        dueAtUtc = default;
        if (!DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) return false;
        dueAtUtc = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUniversalTime();
        return true;
    }

    private static bool TryParseQuantity(string value, int scale, out long scaled)
    {
        return ArabicDisplay.TryParseQuantity(value, scale, true, out scaled);
    }
}
