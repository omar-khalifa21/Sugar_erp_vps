using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using SugarERP.Application;
using SugarERP.Domain;

namespace SugarERP.Desktop.Shared.ViewModels;

public sealed partial class BranchPosViewModel
{
    private readonly IBranchModuleOperations _moduleOperations;
    private readonly IShiftReportWriter _shiftReportWriter;
    private AsyncRelayCommand _saveRequestDraftCommand = null!;
    private AsyncRelayCommand _submitRequestCommand = null!;
    private AsyncRelayCommand _createDemoShipmentCommand = null!;
    private AsyncRelayCommand _receiveShipmentCommand = null!;
    private AsyncRelayCommand _postManualIncomingCommand = null!;
    private AsyncRelayCommand _refundSaleCommand = null!;
    private AsyncRelayCommand _submitReturnCommand = null!;
    private RelayCommand _advanceCloseStepCommand = null!;
    private RelayCommand _previousCloseStepCommand = null!;
    private AsyncRelayCommand _closeShiftCommand = null!;
    private RelayCommand _openReportCommand = null!;
    private AsyncRelayCommand _saveSettingsCommand = null!;
    private AsyncRelayCommand _retryReportCommand = null!;
    private AsyncRelayCommand _testConnectionCommand = null!;
    private AsyncRelayCommand _reprintLastReceiptCommand = null!;
    private AsyncRelayCommand _reprintSaleCommand = null!;
    private AsyncRelayCommand _reprintShiftReportCommand = null!;
    private AsyncRelayCommand _testPrinterCommand = null!;
    private AsyncRelayCommand _saveCatalogItemCommand = null!;
    private AsyncRelayCommand _archiveCatalogItemCommand = null!;
    private AsyncRelayCommand _deleteCatalogItemCommand = null!;
    private RelayCommand _newCatalogItemCommand = null!;
    private RelayCommand<QuantityEntryRowViewModel> _addRequestLineCommand = null!;
    private RelayCommand<QuantityEntryRowViewModel> _addReturnLineCommand = null!;
    private IncomingRowViewModel? _selectedIncoming;
    private SaleHistoryRowViewModel? _selectedSale;
    private SaleLineCorrectionRowViewModel? _selectedSaleLine;
    private RequestHistoryRowViewModel? _selectedRequest;
    private ClosedShiftRowViewModel? _selectedClosedShift;
    private CatalogRowViewModel? _selectedCatalogItem;
    private string _requestStatusText = "اختر الكميات واضغط حفظ الطلب وإرساله. يعمل حتى بدون إنترنت.";
    private string _incomingStatusText = "تظهر هنا الشحنات التي أرسلها المطبخ.";
    private string _manualIncomingReason = string.Empty;
    private string _shiftHistorySummaryText = string.Empty;
    private string _returnStatusText = "مرتجع المطبخ مستند مستقل ويخصم الرصيد مرة واحدة عند الإرسال.";
    private string _closingSummaryText = string.Empty;
    private string _closingReturnReviewText = string.Empty;
    private string _closeReportText = string.Empty;
    private string _historySummaryText = string.Empty;
    private string _settingsStatusText = string.Empty;
    private string _catalogSummaryText = string.Empty;
    private string _correctionQuantityText = "1";
    private string _correctionReason = string.Empty;
    private string _returnReason = string.Empty;
    private string _actualCashText = "0";
    private string _exportDirectory = string.Empty;
    private string _printerName = string.Empty;
    private bool _restockCorrection = true;
    private bool _isRefundCash = true;
    private bool _hasIncomingShipments;
    private bool _isCatalogEditorOpen;
    private int _closeStep = 1;
    private string _catalogSku = string.Empty;
    private string _catalogName = string.Empty;
    private string _catalogUnit = "قطعة";
    private string _catalogScaleText = "1";
    private string _catalogPriceText = "0";

    public ObservableCollection<CatalogRowViewModel> CatalogRows { get; } = [];
    public ObservableCollection<QuantityEntryRowViewModel> RequestRows { get; } = [];
    public ObservableCollection<RequestHistoryRowViewModel> RequestHistoryRows { get; } = [];
    public ObservableCollection<IncomingRowViewModel> IncomingRows { get; } = [];
    public ObservableCollection<IncomingCountRowViewModel> IncomingCountRows { get; } = [];
    public ObservableCollection<QuantityEntryRowViewModel> ManualIncomingRows { get; } = [];
    public ObservableCollection<SaleHistoryRowViewModel> SaleRows { get; } = [];
    public ObservableCollection<SaleLineCorrectionRowViewModel> SaleDetailLines { get; } = [];
    public ObservableCollection<QuantityEntryRowViewModel> ReturnRows { get; } = [];
    public ObservableCollection<QuantityEntryRowViewModel> ClosingRows { get; } = [];
    public ObservableCollection<ClosedShiftRowViewModel> ClosedShiftRows { get; } = [];
    public ObservableCollection<string> InstalledPrinterNames { get; } = [];

    public string CatalogSummaryText { get => _catalogSummaryText; private set => SetProperty(ref _catalogSummaryText, value); }
    public string RequestStatusText { get => _requestStatusText; private set => SetProperty(ref _requestStatusText, value); }
    public string IncomingStatusText { get => _incomingStatusText; private set => SetProperty(ref _incomingStatusText, value); }
    public string ManualIncomingReason { get => _manualIncomingReason; set => SetProperty(ref _manualIncomingReason, value); }
    public string ShiftHistorySummaryText { get => _shiftHistorySummaryText; private set => SetProperty(ref _shiftHistorySummaryText, value); }
    public string ReturnStatusText { get => _returnStatusText; private set => SetProperty(ref _returnStatusText, value); }
    public string ClosingSummaryText { get => _closingSummaryText; private set => SetProperty(ref _closingSummaryText, value); }
    public string ClosingReturnReviewText { get => _closingReturnReviewText; private set => SetProperty(ref _closingReturnReviewText, value); }
    public string CloseReportText { get => _closeReportText; private set => SetProperty(ref _closeReportText, value); }
    public string HistorySummaryText { get => _historySummaryText; private set => SetProperty(ref _historySummaryText, value); }
    public string SettingsStatusText { get => _settingsStatusText; private set => SetProperty(ref _settingsStatusText, value); }

    public CatalogRowViewModel? SelectedCatalogItem
    {
        get => _selectedCatalogItem;
        set
        {
            if (!SetProperty(ref _selectedCatalogItem, value)) return;
            if (value is not null)
            {
                IsCatalogEditorOpen = true;
                CatalogSku = value.Snapshot.Sku;
                CatalogName = value.Snapshot.NameAr;
                CatalogUnit = value.Snapshot.Unit;
                CatalogScaleText = value.Snapshot.QuantityScale.ToString(CultureInfo.InvariantCulture);
                CatalogPriceText = ((decimal)value.Snapshot.RetailPriceMinor / 100m).ToString("0.00", CultureInfo.InvariantCulture);
            }
            OnPropertyChanged(nameof(CatalogEditorTitle));
            OnPropertyChanged(nameof(CanArchiveCatalogItem));
            OnPropertyChanged(nameof(HasSelectedCatalogItem));
            _archiveCatalogItemCommand?.NotifyCanExecuteChanged();
            _deleteCatalogItemCommand?.NotifyCanExecuteChanged();
        }
    }

    public string CatalogSku { get => _catalogSku; set => SetProperty(ref _catalogSku, value); }
    public string CatalogName { get => _catalogName; set => SetProperty(ref _catalogName, value); }
    public string CatalogUnit { get => _catalogUnit; set => SetProperty(ref _catalogUnit, value); }
    public string CatalogScaleText { get => _catalogScaleText; set => SetProperty(ref _catalogScaleText, value); }
    public string CatalogPriceText { get => _catalogPriceText; set => SetProperty(ref _catalogPriceText, value); }
    public string CatalogEditorTitle => SelectedCatalogItem is null ? "إضافة صنف جديد" : "تعديل الصنف";
    public bool CanArchiveCatalogItem => SelectedCatalogItem?.Snapshot.Active == true;
    public bool HasSelectedCatalogItem => SelectedCatalogItem is not null;
    public bool IsCatalogEditorOpen
    {
        get => _isCatalogEditorOpen;
        private set
        {
            if (!SetProperty(ref _isCatalogEditorOpen, value)) return;
            OnPropertyChanged(nameof(IsCatalogEditorClosed));
        }
    }
    public bool IsCatalogEditorClosed => !IsCatalogEditorOpen;

    public IncomingRowViewModel? SelectedIncoming
    {
        get => _selectedIncoming;
        set
        {
            if (!SetProperty(ref _selectedIncoming, value)) return;
            PopulateIncomingCounts(value?.Snapshot);
            OnPropertyChanged(nameof(HasSelectedIncoming));
            _receiveShipmentCommand.NotifyCanExecuteChanged();
        }
    }
    public bool HasSelectedIncoming => SelectedIncoming is not null;

    public SaleHistoryRowViewModel? SelectedSale
    {
        get => _selectedSale;
        set
        {
            if (!SetProperty(ref _selectedSale, value)) return;
            SelectedSaleLine = null;
            SaleDetailLines.Clear();
            if (value is not null) _ = LoadSaleDetailsAsync(value.Id);
            OnPropertyChanged(nameof(HasSelectedSale));
            OnPropertyChanged(nameof(HasNoSelectedSale));
            _refundSaleCommand.NotifyCanExecuteChanged();
            _reprintSaleCommand.NotifyCanExecuteChanged();
        }
    }
    public bool HasSelectedSale => SelectedSale is not null;
    public bool HasNoSelectedSale => SelectedSale is null;

    public SaleLineCorrectionRowViewModel? SelectedSaleLine
    {
        get => _selectedSaleLine;
        set
        {
            if (!SetProperty(ref _selectedSaleLine, value)) return;
            if (value is not null)
            {
                CorrectionQuantityText = ((decimal)value.Snapshot.RemainingRefundableScaled / value.QuantityScale)
                    .ToString("0.###", CultureInfo.InvariantCulture);
                RestockCorrection = true;
            }
            _refundSaleCommand.NotifyCanExecuteChanged();
        }
    }

    public RequestHistoryRowViewModel? SelectedRequest
    {
        get => _selectedRequest;
        set
        {
            if (!SetProperty(ref _selectedRequest, value)) return;
            _submitRequestCommand.NotifyCanExecuteChanged();
        }
    }

    public ClosedShiftRowViewModel? SelectedClosedShift
    {
        get => _selectedClosedShift;
        set
        {
            if (!SetProperty(ref _selectedClosedShift, value)) return;
            _openReportCommand.NotifyCanExecuteChanged();
            _retryReportCommand.NotifyCanExecuteChanged();
            _reprintShiftReportCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(HasPendingReport));
            OnPropertyChanged(nameof(PendingReportText));
        }
    }

    public bool HasPendingReport => SelectedClosedShift is not null && SelectedClosedShift.Snapshot.ReportStatus != "جاهز";
    public string PendingReportText => SelectedClosedShift is null
        ? "لا توجد تقارير معلقة."
        : $"تقرير وردية {SelectedClosedShift.DateText} — {SelectedClosedShift.ReportStatusText}";

    public string CorrectionQuantityText { get => _correctionQuantityText; set => SetProperty(ref _correctionQuantityText, value); }
    public string CorrectionReason { get => _correctionReason; set => SetProperty(ref _correctionReason, value); }
    public string ReturnReason { get => _returnReason; set => SetProperty(ref _returnReason, value); }
    public string ActualCashText { get => _actualCashText; set => SetProperty(ref _actualCashText, value); }
    public string ExportDirectory { get => _exportDirectory; set => SetProperty(ref _exportDirectory, value); }
    public string PrinterName { get => _printerName; set => SetProperty(ref _printerName, value); }

    public bool RestockCorrection { get => _restockCorrection; set => SetProperty(ref _restockCorrection, value); }
    public bool IsRefundCash
    {
        get => _isRefundCash;
        set
        {
            if (!value || !SetProperty(ref _isRefundCash, true)) return;
            OnPropertyChanged(nameof(IsRefundVisa));
        }
    }
    public bool IsRefundVisa
    {
        get => !_isRefundCash;
        set
        {
            if (!value || !SetProperty(ref _isRefundCash, false, nameof(IsRefundCash))) return;
            OnPropertyChanged();
        }
    }

    public int CloseStep
    {
        get => _closeStep;
        private set
        {
            if (!SetProperty(ref _closeStep, value)) return;
            OnPropertyChanged(nameof(IsCloseStep1));
            OnPropertyChanged(nameof(IsCloseStep2));
            OnPropertyChanged(nameof(IsCloseStep3));
            OnPropertyChanged(nameof(IsCloseStep4));
            OnPropertyChanged(nameof(CloseStepText));
            _advanceCloseStepCommand.NotifyCanExecuteChanged();
            _previousCloseStepCommand.NotifyCanExecuteChanged();
            _closeShiftCommand.NotifyCanExecuteChanged();
        }
    }
    public bool IsCloseStep1 => CloseStep == 1;
    public bool IsCloseStep2 => CloseStep == 2;
    public bool IsCloseStep3 => CloseStep == 3;
    public bool IsCloseStep4 => CloseStep == 4;
    public string CloseStepText => $"الخطوة {CloseStep} من 4";

    public bool CanCreateDemoShipment => IsDemo;
    public bool HasIncomingShipments
    {
        get => _hasIncomingShipments;
        private set => SetProperty(ref _hasIncomingShipments, value);
    }

    public ICommand SaveRequestDraftCommand => _saveRequestDraftCommand;
    public ICommand SubmitRequestCommand => _submitRequestCommand;
    public ICommand AddRequestLineCommand => _addRequestLineCommand;
    public ICommand CreateDemoShipmentCommand => _createDemoShipmentCommand;
    public ICommand ReceiveShipmentCommand => _receiveShipmentCommand;
    public ICommand PostManualIncomingCommand => _postManualIncomingCommand;
    public ICommand RefundSaleCommand => _refundSaleCommand;
    public ICommand AddReturnLineCommand => _addReturnLineCommand;
    public ICommand SubmitReturnCommand => _submitReturnCommand;
    public ICommand AdvanceCloseStepCommand => _advanceCloseStepCommand;
    public ICommand PreviousCloseStepCommand => _previousCloseStepCommand;
    public ICommand CloseShiftCommand => _closeShiftCommand;
    public ICommand OpenReportCommand => _openReportCommand;
    public ICommand SaveSettingsCommand => _saveSettingsCommand;
    public ICommand RetryReportCommand => _retryReportCommand;
    public ICommand TestConnectionCommand => _testConnectionCommand;
    public ICommand ReprintLastReceiptCommand => _reprintLastReceiptCommand;
    public ICommand ReprintSaleCommand => _reprintSaleCommand;
    public ICommand ReprintShiftReportCommand => _reprintShiftReportCommand;
    public ICommand TestPrinterCommand => _testPrinterCommand;
    public ICommand NewCatalogItemCommand => _newCatalogItemCommand;
    public ICommand SaveCatalogItemCommand => _saveCatalogItemCommand;
    public ICommand ArchiveCatalogItemCommand => _archiveCatalogItemCommand;
    public ICommand DeleteCatalogItemCommand => _deleteCatalogItemCommand;

    partial void InitializeModuleCommands()
    {
        _addRequestLineCommand = new RelayCommand<QuantityEntryRowViewModel>(row => Increment(row));
        _addReturnLineCommand = new RelayCommand<QuantityEntryRowViewModel>(row => Increment(row));
        _saveRequestDraftCommand = new AsyncRelayCommand(() => SaveRequestAsync(false), () => !IsBusy);
        _submitRequestCommand = new AsyncRelayCommand(SubmitRequestAsync, () => !IsBusy);
        _createDemoShipmentCommand = new AsyncRelayCommand(CreateDemoShipmentAsync, () => IsDemo && !IsBusy);
        _receiveShipmentCommand = new AsyncRelayCommand(ReceiveShipmentAsync, () => SelectedIncoming?.Snapshot.IsReceivable == true && !IsBusy);
        _postManualIncomingCommand = new AsyncRelayCommand(PostManualIncomingAsync, () => IsShiftOpen && !IsBusy);
        _refundSaleCommand = new AsyncRelayCommand(RefundSaleAsync, () => !IsBusy);
        _submitReturnCommand = new AsyncRelayCommand(SubmitReturnAsync, () => IsShiftOpen && !IsBusy);
        _advanceCloseStepCommand = new RelayCommand(AdvanceCloseStep, () => CloseStep < 4);
        _previousCloseStepCommand = new RelayCommand(() => CloseStep -= 1, () => CloseStep > 1);
        _closeShiftCommand = new AsyncRelayCommand(CloseShiftAsync, () => IsShiftOpen && CloseStep == 4 && !IsBusy);
        _openReportCommand = new RelayCommand(OpenSelectedReport, () => !string.IsNullOrWhiteSpace(SelectedClosedShift?.ReportPath));
        _saveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, () => !IsBusy);
        _retryReportCommand = new AsyncRelayCommand(RetrySelectedReportAsync, () => SelectedClosedShift is not null && !IsBusy);
        _testConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => !IsBusy);
        _reprintLastReceiptCommand = new AsyncRelayCommand(ReprintLastReceiptAsync, () => _lastReceiptId.HasValue && !IsBusy);
        _reprintSaleCommand = new AsyncRelayCommand(ReprintSelectedSaleAsync, () => SelectedSale is not null && !IsBusy);
        _reprintShiftReportCommand = new AsyncRelayCommand(ReprintSelectedShiftAsync, () => SelectedClosedShift is not null && !IsBusy);
        _testPrinterCommand = new AsyncRelayCommand(TestPrinterAsync, () => !IsBusy);
        _newCatalogItemCommand = new RelayCommand(NewCatalogItem);
        _saveCatalogItemCommand = new AsyncRelayCommand(SaveCatalogItemAsync, () => !IsBusy);
        _archiveCatalogItemCommand = new AsyncRelayCommand(ArchiveCatalogItemAsync, () => CanArchiveCatalogItem && !IsBusy);
        _deleteCatalogItemCommand = new AsyncRelayCommand(DeleteCatalogItemAsync, () => SelectedCatalogItem is not null && !IsBusy);
        InitializeCustomOrderCommands();
    }

    private void NotifyModuleCommandState()
    {
        if (_saveRequestDraftCommand is null) return;
        _saveRequestDraftCommand.NotifyCanExecuteChanged();
        _submitRequestCommand.NotifyCanExecuteChanged();
        _createDemoShipmentCommand.NotifyCanExecuteChanged();
        _receiveShipmentCommand.NotifyCanExecuteChanged();
        _postManualIncomingCommand.NotifyCanExecuteChanged();
        _refundSaleCommand.NotifyCanExecuteChanged();
        _submitReturnCommand.NotifyCanExecuteChanged();
        _closeShiftCommand.NotifyCanExecuteChanged();
        _saveSettingsCommand.NotifyCanExecuteChanged();
        _retryReportCommand.NotifyCanExecuteChanged();
        _testConnectionCommand.NotifyCanExecuteChanged();
        _reprintLastReceiptCommand.NotifyCanExecuteChanged();
        _reprintSaleCommand.NotifyCanExecuteChanged();
        _reprintShiftReportCommand.NotifyCanExecuteChanged();
        _testPrinterCommand.NotifyCanExecuteChanged();
        _saveCatalogItemCommand.NotifyCanExecuteChanged();
        _archiveCatalogItemCommand.NotifyCanExecuteChanged();
        _deleteCatalogItemCommand.NotifyCanExecuteChanged();
        NotifyCustomOrderCommandState();
        _refreshCurrentPageCommand.NotifyCanExecuteChanged();
    }

    private async Task InitializeModuleDataAsync()
    {
        var settings = await _moduleOperations.GetSettingsAsync();
        if (!string.IsNullOrWhiteSpace(settings.ConnectionAddress))
            ApiBaseUrl = settings.ConnectionAddress;
        ExportDirectory = settings.ExportDirectory;
        PrinterName = settings.PrinterName;
        TouchMode = settings.TouchMode;
        RefreshInstalledPrinters();
        RefreshInstalledPrinters();
    }

    private void RefreshInstalledPrinters()
    {
        InstalledPrinterNames.Clear();
        try
        {
            foreach (var printer in _printer.GetInstalledPrinterNames())
                InstalledPrinterNames.Add(printer);
        }
        catch
        {
            // Printer discovery is a side effect. A Windows spooler failure must
            // never prevent the local database or sales UI from opening.
        }
    }

    private partial async Task RefreshModuleDataAsync(BranchPage page)
    {
        if (!IsEnrolled || page is BranchPage.Menu or BranchPage.Pos) return;
        IsBusy = true;
        try
        {
            switch (page)
            {
                case BranchPage.Catalog:
                    await LoadCatalogAsync();
                    break;
                case BranchPage.CustomOrders:
                    await LoadCustomOrdersAsync();
                    break;
                case BranchPage.Request:
                    await LoadRequestAsync();
                    break;
                case BranchPage.Incoming:
                    await LoadIncomingAsync();
                    break;
                case BranchPage.ShiftHistory:
                    await LoadShiftHistoryAsync();
                    break;
                case BranchPage.KitchenReturn:
                    await LoadReturnAsync();
                    break;
                case BranchPage.CloseShift:
                    await LoadClosingAsync();
                    break;
                case BranchPage.History:
                    await LoadHistoryAsync();
                    break;
                case BranchPage.Settings:
                    await LoadSettingsAsync();
                    break;
            }
        }
        catch (BusinessRuleException exception)
        {
            StatusMessage = exception.UserMessage;
        }
        catch (Exception)
        {
            StatusMessage = "تعذر تحميل هذه الشاشة. البيانات المحفوظة لم تتغير.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadCatalogAsync()
    {
        var selectedId = SelectedCatalogItem?.Id;
        var snapshot = await _moduleOperations.GetCatalogAsync();
        CatalogRows.Clear();
        foreach (var item in snapshot.Items)
        {
            CatalogRows.Add(new CatalogRowViewModel(
                item,
                ArabicDisplay.Quantity(item.QuantityScaled, item.QuantityScale, item.Unit),
                ArabicDisplay.Money(item.RetailPriceMinor),
                item.QuantityScaled < 5L * item.QuantityScale));
        }
        SelectedCatalogItem = selectedId is null ? null : CatalogRows.FirstOrDefault(value => value.Id == selectedId);
        var update = snapshot.LastSyncAtUtc is null ? "لم يصل تحديث بعد" : $"آخر تحديث {snapshot.LastSyncAtUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm}";
        CatalogSummaryText = $"{snapshot.Items.Count(value => value.Active)} نشط · {snapshot.Items.Count(value => !value.Active)} مؤرشف · {snapshot.Holds.Count} تحت المراجعة · {update}";
    }

    private void NewCatalogItem()
    {
        SelectedCatalogItem = null;
        IsCatalogEditorOpen = true;
        CatalogSku = string.Empty;
        CatalogName = string.Empty;
        CatalogUnit = "قطعة";
        CatalogScaleText = "1";
        CatalogPriceText = string.Empty;
        CatalogSummaryText = "أدخل بيانات الصنف. الرصيد يبدأ بصفر ولا يمكن تعديله من هذه الشاشة.";
    }

    private async Task SaveCatalogItemAsync()
    {
        if (!int.TryParse(CatalogScaleText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var scale)
            || !ArabicDisplay.TryParseMoney(CatalogPriceText, out var price)
            || price <= 0)
        {
            CatalogSummaryText = "أدخل سعر بيع أكبر من صفر بالجنيه المصري.";
            return;
        }
        IsBusy = true;
        try
        {
            var commandId = Guid.NewGuid();
            var internalSku = SelectedCatalogItem?.Snapshot.Sku ?? $"AUTO-{commandId:N}";
            var result = await _moduleOperations.SaveCatalogItemAsync(new SaveCatalogItemCommand(
                commandId, SelectedCatalogItem?.Id, SelectedCatalogItem?.Snapshot.Version,
                internalSku, CatalogName, CatalogUnit, scale, price));
            await LoadCatalogAsync();
            SelectedCatalogItem = CatalogRows.FirstOrDefault(value => value.Id == result.Id);
            await RefreshSnapshotAsync();
            CatalogSummaryText = "تم حفظ الصنف.";
        }
        catch (BusinessRuleException exception)
        {
            CatalogSummaryText = exception.UserMessage;
        }
        catch (Exception)
        {
            CatalogSummaryText = "تعذر حفظ الصنف. لم يتغير المخزون.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ArchiveCatalogItemAsync()
    {
        var selected = SelectedCatalogItem;
        if (selected is null) return;
        IsBusy = true;
        try
        {
            await _moduleOperations.ArchiveCatalogItemAsync(Guid.NewGuid(), selected.Id, selected.Snapshot.Version);
            await LoadCatalogAsync();
            await RefreshSnapshotAsync();
            CatalogSummaryText = "تمت أرشفة الصنف محلياً. لم يُحذف أي سجل أو حركة مخزون.";
        }
        catch (BusinessRuleException exception)
        {
            CatalogSummaryText = exception.UserMessage;
        }
        catch (Exception)
        {
            CatalogSummaryText = "تعذرت أرشفة الصنف. لم يتغير شيء.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteCatalogItemAsync()
    {
        var selected = SelectedCatalogItem;
        if (selected is null)
        {
            CatalogSummaryText = "اختر صنفاً للحذف.";
            return;
        }
        IsBusy = true;
        try
        {
            await _moduleOperations.DeleteCatalogItemAsync(Guid.NewGuid(), selected.Id, selected.Snapshot.Version);
            SelectedCatalogItem = null;
            IsCatalogEditorOpen = false;
            await LoadCatalogAsync();
            await RefreshSnapshotAsync();
            CatalogSummaryText = "تم حذف الصنف غير المستخدم نهائياً.";
        }
        catch (BusinessRuleException exception)
        {
            CatalogSummaryText = exception.UserMessage;
        }
        catch (Exception)
        {
            CatalogSummaryText = "تعذر حذف الصنف. لم يتغير شيء.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadRequestAsync()
    {
        var catalog = await _moduleOperations.GetCatalogAsync();
        RequestRows.Clear();
        foreach (var item in catalog.Items.Where(value => value.Active).OrderBy(value => value.NameAr))
            RequestRows.Add(new QuantityEntryRowViewModel(
                item.Id,
                item.NameAr,
                item.Unit,
                item.QuantityScale,
                ArabicDisplay.Quantity(item.QuantityScaled, item.QuantityScale, item.Unit)));
        await ReloadRequestHistoryAsync();
        await LoadIncomingAsync();
    }

    private async Task ReloadRequestHistoryAsync()
    {
        var requests = await _moduleOperations.GetKitchenRequestsAsync();
        RequestHistoryRows.Clear();
        foreach (var request in requests)
        {
            var requested = string.Join("، ", request.Lines.Select(value =>
                $"{value.ItemName} {ArabicDisplay.Quantity(value.RequestedScaled, value.QuantityScale, value.Unit)}"));
            var approved = request.Lines.Sum(value => value.ApprovedScaled ?? 0);
            var sent = request.Lines.Sum(value => value.SentScaled);
            RequestHistoryRows.Add(new RequestHistoryRowViewModel(
                request,
                $"طلب {request.RequestedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}",
                requested,
                RequestDeliveryArabic(request.DeliveryState, request.Status),
                approved == 0 && sent == 0 ? "لم يعتمد أو يشحن بعد" : $"معتمد {approved} · مشحون {sent}"));
        }
        SelectedRequest = RequestHistoryRows.FirstOrDefault();
        RequestStatusText = requests.Count == 0
            ? "لا توجد طلبات وارد بعد. أدخل الكميات واضغط إرسال."
            : $"يوجد {requests.Count} طلب وارد محفوظ. الطلب المرسل لا يزيد المخزون حتى وصول شحنة وعدّها.";
    }

    private async Task SaveRequestAsync(bool submit)
    {
        if (!TryReadPositiveRows(RequestRows, out var lines, out var message))
        {
            RequestStatusText = message;
            return;
        }
        IsBusy = true;
        try
        {
            var result = await _moduleOperations.CreateKitchenRequestAsync(new CreateKitchenRequestCommand(Guid.NewGuid(), lines), submit);
            ClearQuantities(RequestRows);
            await ReloadRequestHistoryAsync();
            await RefreshSnapshotAsync();
            RequestStatusText = submit
                ? "تم حفظ الطلب. سيُرسل تلقائياً عند توفر الاتصال."
                : "تم حفظ طلب الوارد كمسودة محلية ويمكن إرساله لاحقاً.";
            SelectedRequest = RequestHistoryRows.SingleOrDefault(value => value.Id == result.Id);
        }
        catch (BusinessRuleException exception)
        {
            RequestStatusText = exception.UserMessage;
        }
        catch (Exception)
        {
            RequestStatusText = "تعذر حفظ طلب الوارد. لم تتغير الكميات أو المخزون.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SubmitRequestAsync()
    {
        if (TryReadPositiveRows(RequestRows, out _, out _))
        {
            await SaveRequestAsync(true);
            return;
        }
        if (SelectedRequest?.IsDraft == true)
        {
            IsBusy = true;
            try
            {
                await _moduleOperations.SubmitKitchenRequestAsync(SelectedRequest.Id, SelectedRequest.Snapshot.Version);
                await ReloadRequestHistoryAsync();
                await RefreshSnapshotAsync();
                RequestStatusText = "تم حفظ الطلب ووضعه في انتظار الإرسال.";
            }
            catch (BusinessRuleException exception)
            {
                RequestStatusText = exception.UserMessage;
            }
            catch (Exception)
            {
                RequestStatusText = "تعذر الإرسال الآن. الطلب محفوظ محلياً وسيبقى في الانتظار.";
            }
            finally
            {
                IsBusy = false;
            }
            return;
        }
        RequestStatusText = "أدخل كمية لصنف واحد على الأقل ثم اضغط حفظ الطلب وإرساله.";
    }

    private async Task LoadIncomingAsync()
    {
        var catalog = await _moduleOperations.GetCatalogAsync();
        ManualIncomingRows.Clear();
        foreach (var item in catalog.Items.Where(value => value.Active).OrderBy(value => value.NameAr))
            ManualIncomingRows.Add(new QuantityEntryRowViewModel(item.Id, item.NameAr, item.Unit, item.QuantityScale,
                ArabicDisplay.Quantity(item.QuantityScaled, item.QuantityScale, item.Unit)));
        var selectedId = SelectedIncoming?.Id;
        var shipments = await _moduleOperations.GetIncomingShipmentsAsync();
        HasIncomingShipments = shipments.Count > 0;
        IncomingRows.Clear();
        foreach (var shipment in shipments)
        {
            IncomingRows.Add(new IncomingRowViewModel(
                shipment,
                shipment.Reference,
                ShipmentStatusArabic(shipment.Status),
                shipment.DispatchedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                string.Join("، ", shipment.Lines.Select(value =>
                    $"{value.ItemName} {ArabicDisplay.Quantity(value.SentScaled, value.QuantityScale, value.Unit)}")),
                shipment.IsReceivable));
        }
        SelectedIncoming = selectedId is null ? null : IncomingRows.FirstOrDefault(value => value.Id == selectedId);
        IncomingStatusText = shipments.Count == 0
            ? "لا توجد شحنات للاستلام الآن."
            : $"{shipments.Count} طلب وارد · {shipments.Count(value => value.IsReceivable)} بانتظار العد والاستلام.";
    }

    private async Task PostManualIncomingAsync()
    {
        if (!TryReadPositiveRows(ManualIncomingRows, out var lines, out var message)) { IncomingStatusText = message; return; }
        IsBusy = true;
        try
        {
            await _moduleOperations.PostManualIncomingAsync(new PostManualIncomingCommand(Guid.NewGuid(), ManualIncomingReason, lines));
            ClearQuantities(ManualIncomingRows);
            ManualIncomingReason = string.Empty;
            var sync = await _syncService.SynchronizeAsync();
            await RefreshSnapshotAsync();
            await LoadIncomingAsync();
            IncomingStatusText = sync.Succeeded ? "تمت إضافة الوارد اليدوي ومزامنته مع الخادم." : $"تم حفظ الوارد محلياً. {sync.UserMessage}";
        }
        catch (BusinessRuleException exception) { IncomingStatusText = exception.UserMessage; }
        catch { IncomingStatusText = "تعذر حفظ الوارد اليدوي. لم تتغير الكميات."; }
        finally { IsBusy = false; }
    }

    private async Task CreateDemoShipmentAsync()
    {
        IsBusy = true;
        try
        {
            await _moduleOperations.CreateSyntheticDemoShipmentAsync(Guid.NewGuid());
            await LoadIncomingAsync();
            IncomingStatusText = "تم تنزيل شحنة تجريبية مرتبطة بطلب وارد مرسل. أدخل العد الفعلي ثم أكد.";
        }
        catch (BusinessRuleException exception)
        {
            IncomingStatusText = exception.UserMessage;
        }
        catch (Exception)
        {
            IncomingStatusText = "تعذر تجهيز الشحنة التجريبية.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void PopulateIncomingCounts(IncomingShipmentSnapshot? shipment)
    {
        IncomingCountRows.Clear();
        if (shipment is null) return;
        foreach (var line in shipment.Lines) IncomingCountRows.Add(new IncomingCountRowViewModel(line));
    }

    private async Task ReceiveShipmentAsync()
    {
        var shipment = SelectedIncoming?.Snapshot;
        if (shipment is null) return;
        var counts = new List<ShipmentCountInput>();
        foreach (var row in IncomingCountRows)
        {
            if (!ArabicDisplay.TryParseQuantity(row.QuantityText, row.QuantityScale, true, out var quantity))
            {
                IncomingStatusText = $"أدخل عدّاً صحيحاً للصنف {row.Name}.";
                return;
            }
            counts.Add(new ShipmentCountInput(row.ShipmentLineId, quantity));
        }
        IsBusy = true;
        try
        {
            var result = await _moduleOperations.ReceiveShipmentAsync(new ReceiveShipmentCommand(Guid.NewGuid(), shipment.Id, shipment.Version, counts));
            await LoadIncomingAsync();
            await RefreshSnapshotAsync();
            IncomingStatusText = result.EntireShipmentHeld
                ? "يوجد فرق في العد. الشحنة تحت المراجعة ولم تُضف إلى الرصيد المتاح."
                : "تم قبول طلب الوارد وإضافة الكميات المطابقة إلى المخزون مرة واحدة.";
        }
        catch (BusinessRuleException exception)
        {
            IncomingStatusText = exception.UserMessage;
        }
        catch (Exception)
        {
            IncomingStatusText = "تعذر حفظ عد طلب الوارد. لم تتم إضافة أي كمية للمخزون.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadShiftHistoryAsync()
    {
        var selectedId = SelectedSale?.Id;
        var sales = await _moduleOperations.GetCurrentShiftSalesAsync();
        SaleRows.Clear();
        foreach (var sale in sales)
        {
            SaleRows.Add(new SaleHistoryRowViewModel(
                sale,
                sale.ReceiptNumber,
                sale.OccurredAtUtc.ToLocalTime().ToString("HH:mm"),
                $"{PaymentArabic(sale.PaymentMethod)} · {FulfillmentArabic(sale.Fulfillment)}",
                ArabicDisplay.Money(sale.EffectiveTotalMinor),
                sale.RefundedTotalMinor == 0 ? "أصلي" : $"مسترجع {ArabicDisplay.Money(sale.RefundedTotalMinor)}"));
        }
        SelectedSale = selectedId is null ? null : SaleRows.FirstOrDefault(value => value.Id == selectedId);
        ShiftHistorySummaryText = sales.Count == 0
            ? "لا توجد إيصالات في الوردية الحالية."
            : $"{sales.Count} إيصال · صافي حالي {ArabicDisplay.Money(sales.Sum(value => value.EffectiveTotalMinor))}";
    }

    private async Task LoadSaleDetailsAsync(Guid saleId)
    {
        try
        {
            var details = await _moduleOperations.GetSaleAsync(saleId);
            if (SelectedSale?.Id != saleId) return;
            SaleDetailLines.Clear();
            foreach (var line in details.Lines) SaleDetailLines.Add(new SaleLineCorrectionRowViewModel(line));
            SelectedSaleLine = SaleDetailLines.FirstOrDefault(value => value.Snapshot.RemainingRefundableScaled > 0);
            IsRefundCash = details.PaymentMethod == PaymentMethod.Cash;
            IsRefundVisa = details.PaymentMethod == PaymentMethod.Visa;
            ShiftHistorySummaryText = $"{details.ReceiptNumber} · أصلي {ArabicDisplay.Money(details.OriginalTotalMinor)} · مسترجع {ArabicDisplay.Money(details.RefundedTotalMinor)} · الإكرامية الأصلية {ArabicDisplay.Money(details.TipMinor)} لا تتغير تلقائياً.";
        }
        catch (BusinessRuleException exception)
        {
            ShiftHistorySummaryText = exception.UserMessage;
        }
        catch (Exception)
        {
            ShiftHistorySummaryText = "تعذر تحميل تفاصيل الإيصال.";
        }
    }

    private async Task RefundSaleAsync()
    {
        var sale = SelectedSale;
        var line = SelectedSaleLine;
        if (sale is null || line is null)
        {
            ShiftHistorySummaryText = "اختر الإيصال ثم الصنف المراد إرجاعه.";
            return;
        }
        if (!ArabicDisplay.TryParseQuantity(CorrectionQuantityText, line.QuantityScale, false, out var quantity))
        {
            ShiftHistorySummaryText = "أدخل كمية استرجاع صحيحة ضمن دقة وحدة الصنف.";
            return;
        }
        IsBusy = true;
        try
        {
            var result = await _moduleOperations.CorrectSaleAsync(new CorrectSaleCommand(
                Guid.NewGuid(),
                sale.Id,
                line.Id,
                quantity,
                RestockCorrection,
                CorrectionReason,
                IsRefundCash ? PaymentMethod.Cash : PaymentMethod.Visa,
                Environment.UserName));
            CorrectionReason = string.Empty;
            await LoadShiftHistoryAsync();
            await RefreshSnapshotAsync();
            var message = $"تم حفظ تصحيح مرتبط بالأصل ورد {ArabicDisplay.Money(result.RefundMinor)}. {(result.Restocked ? "أعيدت الكمية الصالحة للمخزون." : "لم تعد الكمية التالفة إلى المخزون.")} الإكرامية الأصلية لم تتغير.";
            ShiftHistorySummaryText = await PrintSaleDocumentAsync(result.CorrectionId, sale.Id, message);
        }
        catch (BusinessRuleException exception)
        {
            ShiftHistorySummaryText = exception.UserMessage;
        }
        catch (Exception)
        {
            ShiftHistorySummaryText = "تعذر حفظ التصحيح. الإيصال الأصلي والمخزون لم يتغيرا.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadReturnAsync()
    {
        var catalog = await _moduleOperations.GetCatalogAsync();
        ReturnRows.Clear();
        foreach (var item in catalog.Items.Where(value => value.QuantityScaled > 0).OrderBy(value => value.NameAr))
            ReturnRows.Add(new QuantityEntryRowViewModel(
                item.Id,
                item.NameAr,
                item.Unit,
                item.QuantityScale,
                ArabicDisplay.Quantity(item.QuantityScaled, item.QuantityScale, item.Unit)));
        var history = await _moduleOperations.GetKitchenReturnsAsync();
        ReturnStatusText = history.Count == 0
            ? "لا توجد مرتجعات مطبخ سابقة. اختر كميات واكتب السبب."
            : $"{history.Count} مستند مرتجع · {history.Count(value => value.Status == KitchenReturnStatus.Dispatched)} بانتظار إقرار المطبخ.";
    }

    private async Task SubmitReturnAsync()
    {
        if (!TryReadPositiveRows(ReturnRows, out var lines, out var message))
        {
            ReturnStatusText = message;
            return;
        }
        IsBusy = true;
        try
        {
            var result = await _moduleOperations.DispatchKitchenReturnAsync(new DispatchKitchenReturnCommand(Guid.NewGuid(), ReturnReason, lines));
            ReturnReason = string.Empty;
            await LoadReturnAsync();
            await RefreshSnapshotAsync();
            ReturnStatusText = $"تم حفظ المرتجع {result.Reference} وخصم الكمية مرة واحدة. ينتظر إقرار المطبخ.";
            if (CurrentPage == BranchPage.CloseShift)
            {
                await LoadClosingAsync();
                ReturnStatusText = $"تم إضافة المرتجع {result.Reference} أثناء الإغلاق وخصم الكمية مرة واحدة.";
            }
        }
        catch (BusinessRuleException exception)
        {
            ReturnStatusText = exception.UserMessage;
        }
        catch (Exception)
        {
            ReturnStatusText = "تعذر حفظ مرتجع المطبخ. لم تُخصم أي كمية.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadClosingAsync()
    {
        if (!IsShiftOpen)
        {
            ClosingRows.Clear();
            ClosingSummaryText = "لا توجد وردية مفتوحة.";
            return;
        }
        var preview = await _moduleOperations.GetClosingPreviewAsync();
        await LoadReturnAsync();
        var kitchenReturns = await _moduleOperations.GetKitchenReturnsAsync();
        ClosingRows.Clear();
        foreach (var item in preview.Items)
        {
            var row = new QuantityEntryRowViewModel(
                item.ItemId,
                item.ItemName,
                item.Unit,
                item.QuantityScale,
                string.Empty,
                ArabicDisplay.Quantity(item.ExpectedScaled, item.QuantityScale, item.Unit))
            {
                QuantityText = ((decimal)item.ExpectedScaled / item.QuantityScale).ToString("0.###", CultureInfo.InvariantCulture),
                OpeningText = ArabicDisplay.Quantity(item.OpeningScaled, item.QuantityScale, item.Unit),
                IncomingText = ArabicDisplay.Quantity(item.IncomingScaled, item.QuantityScale, item.Unit)
            };
            ClosingRows.Add(row);
        }
        ActualCashText = ((decimal)preview.ExpectedCashMinor / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        var pendingReturns = kitchenReturns.Where(value => value.Status == KitchenReturnStatus.Dispatched).ToArray();
        var pendingSync = (await _operations.GetSnapshotAsync()).PendingOutboxCount;
        ClosingReturnReviewText = pendingReturns.Length == 0
            ? "لا توجد مرتجعات مطبخ معلقة. المرتجعات المرسلة سابقاً خُصمت من المخزون مرة واحدة بالفعل."
            : $"يوجد {pendingReturns.Length} مرتجع مطبخ بانتظار إقرار المطبخ: {string.Join("، ", pendingReturns.Select(value => value.Reference))}. لن تُخصم هذه الكميات مرة ثانية عند الإغلاق.";
        ClosingSummaryText = $"متوقع نقدي {ArabicDisplay.Money(preview.ExpectedCashMinor)} · نقدي {ArabicDisplay.Money(preview.CashSalesMinor)} · فيزا {ArabicDisplay.Money(preview.VisaSalesMinor)} · {preview.ReceiptCount} إيصال · {preview.PendingHoldCount} تحت المراجعة · {preview.PendingReturnCount} مرتجع معلق · {pendingSync} حركة بانتظار المزامنة";
        CloseReportText = pendingSync > 0
            ? $"تنبيه: {pendingSync} حركة محلية لم تصل للخادم بعد. يمكن إغلاق الوردية بأمان وستبقى الحركات محفوظة للمزامنة لاحقاً."
            : "الكميات المتبقية محسوبة تلقائياً من الوارد والمبيعات والمرتجعات. الإغلاق ينشئ تقرير Excel قابلاً لإعادة المحاولة.";
        CloseStep = 1;
    }

    private void AdvanceCloseStep()
    {
        if (CloseStep == 2)
        {
            CloseReportText = "تم حساب المتبقي تلقائياً. إذا كان الواقع مختلفاً، أصلح الحركة الناقصة قبل الإغلاق من البيع أو الوارد أو المرتجع.";
        }
        if (CloseStep == 3 && !ArabicDisplay.TryParseMoney(ActualCashText, out _))
        {
            CloseReportText = "أدخل النقدية الفعلية بقيمة صحيحة.";
            return;
        }
        CloseStep += 1;
    }

    private async Task CloseShiftAsync()
    {
        if (!TryReadAllCountRows(ClosingRows, out var counts, out var countError))
        {
            CloseReportText = countError;
            return;
        }
        if (!ArabicDisplay.TryParseMoney(ActualCashText, out var actualCash))
        {
            CloseReportText = "أدخل النقدية الفعلية بقيمة صحيحة.";
            return;
        }
        IsBusy = true;
        try
        {
            var result = await _moduleOperations.CloseShiftAsync(new CloseShiftCommand(Guid.NewGuid(), counts, actualCash));
            try
            {
                if (string.IsNullOrWhiteSpace(ExportDirectory))
                    ExportDirectory = (await _moduleOperations.GetSettingsAsync()).ExportDirectory;
                var written = await _shiftReportWriter.WriteAsync(result.Report, ExportDirectory);
                await _moduleOperations.MarkReportSucceededAsync(result.ExportJobId, written);
                CloseReportText = $"تم إغلاق الوردية وإنشاء تقرير Excel ثابت: {written.Path}";
            }
            catch (Exception reportError)
            {
                await _moduleOperations.MarkReportFailedAsync(result.ExportJobId, reportError.Message);
                CloseReportText = "تم إغلاق الوردية بأمان، لكن فشل إنشاء Excel. المهمة محفوظة ويمكن إعادة المحاولة من الإعدادات.";
            }
            CloseReportText = await PrintShiftDocumentAsync(result.ShiftId, result.PrintJobId, CloseReportText, resetJob: false);
            await RefreshSnapshotAsync();
            CurrentPage = BranchPage.Menu;
            StatusMessage = CloseReportText;
        }
        catch (BusinessRuleException exception)
        {
            CloseReportText = exception.UserMessage;
        }
        catch (Exception)
        {
            CloseReportText = "تعذر إغلاق الوردية. بقيت مفتوحة ولم تتغير الأرصدة.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadHistoryAsync()
    {
        var shifts = await _moduleOperations.GetClosedShiftsAsync();
        ClosedShiftRows.Clear();
        foreach (var shift in shifts)
        {
            ClosedShiftRows.Add(new ClosedShiftRowViewModel(
                shift,
                shift.BusinessDate,
                shift.Kind == ShiftKind.Morning ? "صباحية" : "مسائية",
                $"مبيعات {ArabicDisplay.Money(shift.SalesMinor)} · مرتجعات {ArabicDisplay.Money(shift.RefundsMinor)} · {shift.ReceiptCount} إيصال",
                ArabicDisplay.Money(shift.ActualCashMinor - shift.ExpectedCashMinor),
                shift.ReportStatus,
                shift.ReportPath));
        }
        SelectedClosedShift = ClosedShiftRows.FirstOrDefault();
        var years = shifts.Select(value => value.BusinessDate[..4]).Distinct().Count();
        HistorySummaryText = shifts.Count == 0
            ? "لا توجد ورديات مغلقة بعد."
            : $"{shifts.Count} وردية مغلقة عبر {years} سنة · اختر وردية لفتح تقريرها الأصلي.";
    }

    private void OpenSelectedReport()
    {
        var path = SelectedClosedShift?.ReportPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            HistorySummaryText = "تقرير هذه الوردية لم يُنشأ بعد. استخدم إعادة المحاولة.";
            return;
        }
        if (!Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            HistorySummaryText = "ملف تقرير Excel غير موجود في مساره المحفوظ.";
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            HistorySummaryText = "تم فتح نسخة تقرير Excel الأصلية دون تعديلها.";
        }
        catch (Exception)
        {
            HistorySummaryText = "تعذر فتح Excel تلقائياً. الملف ما زال محفوظاً في المسار الظاهر.";
        }
    }

    private async Task RetrySelectedReportAsync()
    {
        var shift = SelectedClosedShift;
        if (shift is null) return;
        IsBusy = true;
        try
        {
            var jobId = await _moduleOperations.GetOrResetExportJobAsync(shift.Id);
            var report = await _moduleOperations.GetShiftReportDataAsync(shift.Id);
            var written = await _shiftReportWriter.WriteAsync(report, ExportDirectory);
            await _moduleOperations.MarkReportSucceededAsync(jobId, written);
            await LoadSettingsAsync();
            SettingsStatusText = $"تم إنشاء تقرير Excel والتحقق من ثباته: {written.Path}";
        }
        catch (BusinessRuleException exception)
        {
            SettingsStatusText = exception.UserMessage;
        }
        catch (Exception exception)
        {
            SettingsStatusText = $"تعذر إنشاء التقرير: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadSettingsAsync()
    {
        var settings = await _moduleOperations.GetSettingsAsync();
        if (!string.IsNullOrWhiteSpace(settings.ConnectionAddress))
            ApiBaseUrl = settings.ConnectionAddress;
        ExportDirectory = settings.ExportDirectory;
        PrinterName = settings.PrinterName;
        TouchMode = settings.TouchMode;
        var shifts = await _moduleOperations.GetClosedShiftsAsync();
        var pending = shifts.FirstOrDefault(value => value.ReportStatus != "جاهز");
        SelectedClosedShift = pending is null
            ? null
            : new ClosedShiftRowViewModel(
                pending,
                pending.BusinessDate,
                pending.Kind == ShiftKind.Morning ? "صباحية" : "مسائية",
                ArabicDisplay.Money(pending.SalesMinor - pending.RefundsMinor),
                ArabicDisplay.Money(pending.ActualCashMinor - pending.ExpectedCashMinor),
                pending.ReportStatus,
                pending.ReportPath);
        SettingsStatusText = "يمكنك تعديل عنوان الاتصال ومكان حفظ تقرير Excel.";
    }

    private async Task SaveSettingsAsync()
    {
        IsBusy = true;
        try
        {
            await _moduleOperations.SaveSettingsAsync(new SaveBranchSettingsCommand(ApiBaseUrl, ExportDirectory, PrinterName, TouchMode));
            SettingsStatusText = "تم حفظ عنوان الاتصال ومجلد Excel.";
        }
        catch (BusinessRuleException exception)
        {
            SettingsStatusText = exception.UserMessage;
        }
        catch (Exception)
        {
            SettingsStatusText = "تعذر حفظ الإعدادات.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task TestConnectionAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _syncService.TestConnectionAsync();
            SettingsStatusText = $"{result.UserMessage} health: {(result.HealthAvailable ? "متاح" : "غير متاح")} · ready: {(result.ReadyAvailable ? "جاهز" : "غير جاهز")}";
        }
        catch (Exception)
        {
            SettingsStatusText = "تعذر اختبار الخادم الآن.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task TestPrinterAsync()
    {
        IsBusy = true;
        try
        {
            RefreshInstalledPrinters();
            if (InstalledPrinterNames.Count == 0 && string.IsNullOrWhiteSpace(PrinterName))
            {
                SettingsStatusText = "لا توجد طابعة فعلية متاحة في Windows. احفظ اسم الطابعة بعد تثبيت تعريفها.";
                return;
            }
            await _printer.PrintTestAsync(PrinterName, SiteName);
            SettingsStatusText = "تم إرسال صفحة اختبار عربية إلى الطابعة.";
        }
        catch (Exception exception)
        {
            SettingsStatusText = $"فشل اختبار الطابعة: {SafeSideEffectMessage(exception)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReprintLastReceiptAsync()
    {
        if (_lastReceiptId is not { } saleId) return;
        IsBusy = true;
        try
        {
            StatusMessage = await PrintSaleDocumentAsync(saleId, saleId, "الإيصال محفوظ ولم تُنشأ عملية بيع جديدة.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReprintSelectedSaleAsync()
    {
        var sale = SelectedSale;
        if (sale is null) return;
        IsBusy = true;
        try
        {
            ShiftHistorySummaryText = await PrintSaleDocumentAsync(sale.Id, sale.Id, "إعادة الطباعة تستخدم الإيصال المحفوظ ولا تنشئ بيعاً جديداً.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReprintSelectedShiftAsync()
    {
        var shift = SelectedClosedShift;
        if (shift is null) return;
        IsBusy = true;
        try
        {
            var jobId = await _moduleOperations.GetOrResetPrintJobAsync(shift.Id, SideEffectKind.PrintShiftReport);
            HistorySummaryText = await PrintShiftDocumentAsync(shift.Id, jobId, "إعادة الطباعة تستخدم لقطة الوردية المغلقة الأصلية.", resetJob: false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task TryPrintSaleAsync(Guid saleId, string savedMessage)
    {
        StatusMessage = await PrintSaleDocumentAsync(saleId, saleId, savedMessage);
    }

    private async Task<string> PrintSaleDocumentAsync(Guid printSourceId, Guid saleId, string prefix)
    {
        if (InstalledPrinterNames.Count == 0 && string.IsNullOrWhiteSpace(PrinterName))
            return $"{prefix} مهمة الطباعة محفوظة؛ لا توجد طابعة فعلية متاحة حالياً.";
        Guid jobId;
        try
        {
            jobId = await _moduleOperations.GetOrResetPrintJobAsync(printSourceId, SideEffectKind.PrintReceipt);
            var sale = await _moduleOperations.GetSaleAsync(saleId);
            await _printer.PrintSaleAsync(PrinterName, SiteName, sale);
            await _moduleOperations.MarkPrintSucceededAsync(jobId);
            return $"{prefix} تمت الطباعة بنجاح.";
        }
        catch (Exception exception)
        {
            try
            {
                jobId = await _moduleOperations.GetOrResetPrintJobAsync(printSourceId, SideEffectKind.PrintReceipt);
                await _moduleOperations.MarkPrintFailedAsync(jobId, exception.Message);
            }
            catch
            {
                // The accounting document remains authoritative even when job
                // status persistence itself cannot be updated.
            }
            return $"{prefix} فشلت الطباعة ويمكن إعادتها دون تكرار البيع: {SafeSideEffectMessage(exception)}";
        }
    }

    private async Task<string> PrintShiftDocumentAsync(Guid shiftId, Guid printJobId, string prefix, bool resetJob)
    {
        if (InstalledPrinterNames.Count == 0 && string.IsNullOrWhiteSpace(PrinterName))
            return $"{prefix} ملخص الطباعة محفوظ لإعادة المحاولة بعد إعداد الطابعة.";
        try
        {
            if (resetJob)
                printJobId = await _moduleOperations.GetOrResetPrintJobAsync(shiftId, SideEffectKind.PrintShiftReport);
            var report = await _moduleOperations.GetShiftReportDataAsync(shiftId);
            await _printer.PrintShiftReportAsync(PrinterName, report);
            await _moduleOperations.MarkPrintSucceededAsync(printJobId);
            return $"{prefix} وتمت طباعة ملخص الوردية.";
        }
        catch (Exception exception)
        {
            try { await _moduleOperations.MarkPrintFailedAsync(printJobId, exception.Message); }
            catch { }
            return $"{prefix} فشلت طباعة الملخص وبقيت قابلة للإعادة: {SafeSideEffectMessage(exception)}";
        }
    }

    private static string SafeSideEffectMessage(Exception exception)
    {
        var text = exception is BusinessRuleException rule ? rule.UserMessage : exception.Message;
        return string.IsNullOrWhiteSpace(text) ? "خطأ غير معروف" : text.Trim().ReplaceLineEndings(" ")[..Math.Min(240, text.Trim().ReplaceLineEndings(" ").Length)];
    }

    private static void Increment(QuantityEntryRowViewModel? row)
    {
        if (row is null) return;
        _ = ArabicDisplay.TryParseQuantity(row.QuantityText, row.QuantityScale, true, out var current);
        row.QuantityText = ((decimal)(current + row.QuantityScale) / row.QuantityScale).ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static bool TryReadPositiveRows(
        IEnumerable<QuantityEntryRowViewModel> rows,
        out IReadOnlyList<QuantityInput> inputs,
        out string message)
    {
        var result = new List<QuantityInput>();
        foreach (var row in rows)
        {
            if (!ArabicDisplay.TryParseQuantity(row.QuantityText, row.QuantityScale, true, out var quantity))
            {
                inputs = [];
                message = $"أدخل كمية صحيحة للصنف {row.Name}.";
                return false;
            }
            if (quantity > 0) result.Add(new QuantityInput(row.ItemId, quantity));
        }
        if (result.Count == 0)
        {
            inputs = [];
            message = "أدخل كمية موجبة لصنف واحد على الأقل.";
            return false;
        }
        inputs = result;
        message = string.Empty;
        return true;
    }

    private static bool TryReadAllCountRows(
        IEnumerable<QuantityEntryRowViewModel> rows,
        out IReadOnlyList<ClosingCountInput> inputs,
        out string message)
    {
        var result = new List<ClosingCountInput>();
        foreach (var row in rows)
        {
            if (!ArabicDisplay.TryParseQuantity(row.QuantityText, row.QuantityScale, true, out var quantity))
            {
                inputs = [];
                message = $"أدخل العد الفعلي الصحيح للصنف {row.Name}، ويمكن أن يكون صفراً.";
                return false;
            }
            result.Add(new ClosingCountInput(row.ItemId, quantity));
        }
        if (result.Count == 0)
        {
            inputs = [];
            message = "لا توجد أصناف ضمن لقطة الوردية.";
            return false;
        }
        inputs = result;
        message = string.Empty;
        return true;
    }

    private static bool TryRowDifference(QuantityEntryRowViewModel row, out long difference)
    {
        difference = 0;
        if (!ArabicDisplay.TryParseQuantity(row.QuantityText, row.QuantityScale, true, out var actual)) return false;
        var expectedNumber = row.ExpectedText.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!ArabicDisplay.TryParseQuantity(expectedNumber, row.QuantityScale, true, out var expected)) return false;
        difference = actual - expected;
        return true;
    }

    private static void ClearQuantities(IEnumerable<QuantityEntryRowViewModel> rows)
    {
        foreach (var row in rows) row.QuantityText = "0";
    }

    private static string RequestStatusArabic(KitchenRequestStatus status) => status switch
    {
        KitchenRequestStatus.Draft => "مسودة",
        KitchenRequestStatus.Submitted => "مرسل للمطبخ",
        KitchenRequestStatus.Approved => "معتمد",
        KitchenRequestStatus.Rejected => "مرفوض",
        KitchenRequestStatus.Partial => "شحن جزئي",
        KitchenRequestStatus.Fulfilled => "مكتمل الشحن",
        _ => "مغلق"
    };

    private static string RequestDeliveryArabic(RequestDeliveryState deliveryState, KitchenRequestStatus status) => deliveryState switch
    {
        RequestDeliveryState.Draft => "لم يُرسل بعد",
        RequestDeliveryState.Waiting => "◷ في انتظار الإرسال",
        RequestDeliveryState.Sent => "✓ تم الإرسال",
        RequestDeliveryState.Failed => "⚠ تعذر الإرسال — محفوظ محلياً",
        _ => $"✓✓ {RequestStatusArabic(status)}"
    };

    private static string ShipmentStatusArabic(ShipmentStatus status) => status switch
    {
        ShipmentStatus.Dispatched or ShipmentStatus.AwaitingReceipt => "بانتظار العد",
        ShipmentStatus.Received => "مستلم",
        ShipmentStatus.Disputed => "تحت المراجعة",
        ShipmentStatus.Decided => "صدر قرار",
        ShipmentStatus.PendingSiteApply => "ينتظر التطبيق بالفرع",
        _ => "تمت التسوية"
    };

    private static string PaymentArabic(PaymentMethod method) => method == PaymentMethod.Cash ? "نقدي" : "فيزا";
    private static string FulfillmentArabic(FulfillmentKind fulfillment) => fulfillment == FulfillmentKind.Table ? "طاولة" : "تيك أواي";
}
