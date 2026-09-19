using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using SugarERP.Application;

namespace SugarERP.Desktop.Shared.ViewModels;

public sealed class CartLineViewModel : ViewModelBase
{
    private readonly Action<CartLineViewModel> _remove;
    private readonly Action _changed;
    private long _quantityScaled;

    public CartLineViewModel(
        CatalogItemSnapshot item,
        Action<CartLineViewModel> remove,
        Action changed)
    {
        Item = item;
        _remove = remove;
        _changed = changed;
        _quantityScaled = item.QuantityScale;
        IncreaseCommand = new RelayCommand(Increase, CanIncrease);
        DecreaseCommand = new RelayCommand(Decrease);
    }

    public CatalogItemSnapshot Item { get; }
    public Guid ItemId => Item.Id;
    public string Name => Item.NameAr;
    public string UnitPrice => ArabicDisplay.Money(Item.RetailPriceMinor);
    public long QuantityScaled => _quantityScaled;
    public long LineTotalMinor => ArabicDisplay.RoundMinor((decimal)Item.RetailPriceMinor * _quantityScaled / Item.QuantityScale);
    public string Quantity => ArabicDisplay.Quantity(_quantityScaled, Item.QuantityScale, Item.Unit);
    public string LineTotal => ArabicDisplay.Money(LineTotalMinor);
    public ICommand IncreaseCommand { get; }
    public ICommand DecreaseCommand { get; }

    private bool CanIncrease() => _quantityScaled + Item.QuantityScale <= Item.QuantityScaled;

    private void Increase()
    {
        _quantityScaled += Item.QuantityScale;
        NotifyQuantityChanged();
    }

    private void Decrease()
    {
        if (_quantityScaled <= Item.QuantityScale)
        {
            _remove(this);
            _changed();
            return;
        }

        _quantityScaled -= Item.QuantityScale;
        NotifyQuantityChanged();
    }

    private void NotifyQuantityChanged()
    {
        OnPropertyChanged(nameof(QuantityScaled));
        OnPropertyChanged(nameof(Quantity));
        OnPropertyChanged(nameof(LineTotalMinor));
        OnPropertyChanged(nameof(LineTotal));
        ((RelayCommand)IncreaseCommand).NotifyCanExecuteChanged();
        _changed();
    }
}
