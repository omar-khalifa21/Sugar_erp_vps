using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace SugarERP.Desktop.Shared.ViewModels;

public sealed partial class BranchPosViewModel
{
    private RelayCommand _showCatalogCommand = null!;
    private RelayCommand _showCustomOrdersCommand = null!;
    private RelayCommand _showRequestCommand = null!;
    private RelayCommand _showIncomingCommand = null!;
    private RelayCommand _showShiftHistoryCommand = null!;
    private RelayCommand _showReturnCommand = null!;
    private RelayCommand _showCloseCommand = null!;
    private RelayCommand _showHistoryCommand = null!;
    private RelayCommand _showSettingsCommand = null!;
    private AsyncRelayCommand _refreshCurrentPageCommand = null!;

    public bool IsCatalogVisible => IsEnrolled && CurrentPage == BranchPage.Catalog;
    public bool IsCustomOrdersVisible => IsEnrolled && CurrentPage == BranchPage.CustomOrders;
    public bool IsRequestVisible => IsEnrolled && CurrentPage == BranchPage.Request;
    public bool IsIncomingVisible => IsEnrolled && CurrentPage == BranchPage.Incoming;
    public bool IsShiftHistoryVisible => IsEnrolled && CurrentPage == BranchPage.ShiftHistory;
    public bool IsReturnVisible => IsEnrolled && CurrentPage == BranchPage.KitchenReturn;
    public bool IsCloseVisible => IsEnrolled && CurrentPage == BranchPage.CloseShift;
    public bool IsHistoryVisible => IsEnrolled && CurrentPage == BranchPage.History;
    public bool IsSettingsVisible => CurrentPage == BranchPage.Settings;

    public string CurrentPageTitle => CurrentPage switch
    {
        BranchPage.Pos => "نقطة البيع",
        BranchPage.CustomOrders => "الطلبات الخاصة",
        BranchPage.Catalog => "الأصناف والمخزون",
        BranchPage.Request => "وارد — طلب",
        BranchPage.Incoming => "وارد — استلام",
        BranchPage.ShiftHistory => "نقطة البيع — حركات الوردية",
        BranchPage.KitchenReturn => "نقطة البيع — مرتجع للمطبخ",
        BranchPage.CloseShift => "نقطة البيع — إغلاق الوردية",
        BranchPage.History => "",
        BranchPage.Settings => "الإعدادات",
        _ => "القائمة الرئيسية"
    };

    public ICommand ShowCatalogCommand => _showCatalogCommand;
    public ICommand ShowCustomOrdersCommand => _showCustomOrdersCommand;
    public ICommand ShowRequestCommand => _showRequestCommand;
    public ICommand ShowIncomingCommand => _showIncomingCommand;
    public ICommand ShowShiftHistoryCommand => _showShiftHistoryCommand;
    public ICommand ShowReturnCommand => _showReturnCommand;
    public ICommand ShowCloseCommand => _showCloseCommand;
    public ICommand ShowHistoryCommand => _showHistoryCommand;
    public ICommand ShowSettingsCommand => _showSettingsCommand;
    public ICommand RefreshCurrentPageCommand => _refreshCurrentPageCommand;

    private void InitializeModuleNavigation()
    {
        _showCatalogCommand = BuildNavigationCommand(BranchPage.Catalog);
        _showCustomOrdersCommand = BuildNavigationCommand(BranchPage.CustomOrders);
        _showRequestCommand = BuildNavigationCommand(BranchPage.Request);
        _showIncomingCommand = BuildNavigationCommand(BranchPage.Incoming);
        _showShiftHistoryCommand = BuildNavigationCommand(BranchPage.ShiftHistory);
        _showReturnCommand = BuildNavigationCommand(BranchPage.KitchenReturn);
        _showCloseCommand = BuildNavigationCommand(BranchPage.CloseShift);
        _showHistoryCommand = BuildNavigationCommand(BranchPage.History);
        _showSettingsCommand = BuildNavigationCommand(BranchPage.Settings);
        _refreshCurrentPageCommand = new AsyncRelayCommand(() => RefreshModulePageAsync(CurrentPage), () => !IsBusy);
        InitializeModuleCommands();
    }

    private RelayCommand BuildNavigationCommand(BranchPage page) => new(() => Navigate(page));

    private void Navigate(BranchPage page)
    {
        CurrentPage = page;
        _ = RefreshModulePageAsync(page);
    }

    private Task RefreshModulePageAsync(BranchPage page) => RefreshModuleDataAsync(page);

    partial void InitializeModuleCommands();
    private partial Task RefreshModuleDataAsync(BranchPage page);
}
