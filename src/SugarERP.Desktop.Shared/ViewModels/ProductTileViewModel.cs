using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using SugarERP.Application;

namespace SugarERP.Desktop.Shared.ViewModels;

public sealed class ProductTileViewModel
{
    public ProductTileViewModel(CatalogItemSnapshot item, Action<ProductTileViewModel> addToCart)
    {
        Item = item;
        AddToCartCommand = new RelayCommand(
            () => addToCart(this),
            () => item.Active && item.RetailPriceMinor > 0 && item.QuantityScaled >= item.QuantityScale);
    }

    public CatalogItemSnapshot Item { get; }
    public Guid Id => Item.Id;
    public string Name => Item.NameAr;
    public string Sku => Item.Sku;
    public string Price => ArabicDisplay.Money(Item.RetailPriceMinor);
    public string Available => ArabicDisplay.Quantity(Item.QuantityScaled, Item.QuantityScale, Item.Unit);
    public bool IsAvailable => Item.Active && Item.RetailPriceMinor > 0 && Item.QuantityScaled >= Item.QuantityScale;
    public string AvailabilityLabel => IsAvailable ? $"متاح {Available}" : "غير متاح";
    public ICommand AddToCartCommand { get; }
}
