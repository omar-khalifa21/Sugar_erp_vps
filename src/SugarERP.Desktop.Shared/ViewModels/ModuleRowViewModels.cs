using SugarERP.Application;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SugarERP.Desktop.Shared.ViewModels;

public sealed class CatalogRowViewModel(
    CatalogItemSnapshot snapshot,
    string availableText,
    string priceText,
    bool isLowStock)
{
    public CatalogItemSnapshot Snapshot { get; } = snapshot;
    public Guid Id => Snapshot.Id;
    public string Name => Snapshot.NameAr;
    public string Sku => Snapshot.Sku;
    public string Unit => Snapshot.Unit;
    public string AvailableText { get; } = availableText;
    public string PriceText { get; } = priceText;
    public bool IsLowStock { get; } = isLowStock;
    public string ActiveText => Snapshot.Active ? "نشط" : "مؤرشف";
}

public sealed class QuantityEntryRowViewModel : ViewModelBase
{
    private string _quantityText = "0";

    public QuantityEntryRowViewModel(
        Guid itemId,
        string name,
        string unit,
        int quantityScale,
        string availableText,
        string expectedText = "")
    {
        ItemId = itemId;
        Name = name;
        Unit = unit;
        QuantityScale = quantityScale;
        AvailableText = availableText;
        ExpectedText = expectedText;
    }

    public Guid ItemId { get; }
    public string Name { get; }
    public string Unit { get; }
    public int QuantityScale { get; }
    public string AvailableText { get; }
    public string ExpectedText { get; }
    public string OpeningText { get; init; } = string.Empty;
    public string IncomingText { get; init; } = string.Empty;

    public string QuantityText
    {
        get => _quantityText;
        set => SetProperty(ref _quantityText, value);
    }
}

public sealed class IncomingRowViewModel(
    IncomingShipmentSnapshot snapshot,
    string reference,
    string statusText,
    string dispatchedText,
    string linesText,
    bool isReceivable)
{
    public IncomingShipmentSnapshot Snapshot { get; } = snapshot;
    public Guid Id => Snapshot.Id;
    public string Reference { get; } = reference;
    public string StatusText { get; } = statusText;
    public string DispatchedText { get; } = dispatchedText;
    public string LinesText { get; } = linesText;
    public bool IsReceivable { get; } = isReceivable;
}

public sealed class SaleHistoryRowViewModel(
    SaleListItemSnapshot snapshot,
    string receiptNumber,
    string occurredText,
    string tenderText,
    string totalText,
    string statusText)
{
    public SaleListItemSnapshot Snapshot { get; } = snapshot;
    public Guid Id => Snapshot.Id;
    public string ReceiptNumber { get; } = receiptNumber;
    public string OccurredText { get; } = occurredText;
    public string TenderText { get; } = tenderText;
    public string TotalText { get; } = totalText;
    public string StatusText { get; } = statusText;
}

public sealed class ClosedShiftRowViewModel(
    ClosedShiftSnapshot snapshot,
    string dateText,
    string kindText,
    string salesText,
    string differenceText,
    string reportStatusText,
    string? reportPath)
{
    public ClosedShiftSnapshot Snapshot { get; } = snapshot;
    public Guid Id => Snapshot.Id;
    public string DateText { get; } = dateText;
    public string KindText { get; } = kindText;
    public string SalesText { get; } = salesText;
    public string DifferenceText { get; } = differenceText;
    public string ReportStatusText { get; } = reportStatusText;
    public string? ReportPath { get; } = reportPath;
}

public sealed class RequestHistoryRowViewModel(
    KitchenRequestSnapshot snapshot,
    string reference,
    string requestedText,
    string statusText,
    string fulfillmentText)
{
    public KitchenRequestSnapshot Snapshot { get; } = snapshot;
    public Guid Id => Snapshot.Id;
    public string Reference { get; } = reference;
    public string RequestedText { get; } = requestedText;
    public string StatusText { get; } = statusText;
    public string FulfillmentText { get; } = fulfillmentText;
    public bool IsDraft => Snapshot.Status == SugarERP.Domain.KitchenRequestStatus.Draft;
}

public sealed class IncomingCountRowViewModel : ViewModelBase
{
    private string _quantityText;

    public IncomingCountRowViewModel(ShipmentLineSnapshot snapshot)
    {
        Snapshot = snapshot;
        _quantityText = ((decimal)snapshot.SentScaled / snapshot.QuantityScale).ToString("0.###");
    }

    public ShipmentLineSnapshot Snapshot { get; }
    public Guid ShipmentLineId => Snapshot.Id;
    public string Name => Snapshot.ItemName;
    public string Unit => Snapshot.Unit;
    public int QuantityScale => Snapshot.QuantityScale;
    public string SentText => ArabicDisplay.Quantity(Snapshot.SentScaled, Snapshot.QuantityScale, Snapshot.Unit);
    public string QuantityText
    {
        get => _quantityText;
        set => SetProperty(ref _quantityText, value);
    }
}

public sealed class SaleLineCorrectionRowViewModel(SaleLineDetails snapshot)
{
    public SaleLineDetails Snapshot { get; } = snapshot;
    public Guid Id => Snapshot.Id;
    public string Name => Snapshot.ItemName;
    public string Unit => Snapshot.Unit;
    public int QuantityScale => Snapshot.QuantityScale;
    public string OriginalText => ArabicDisplay.Quantity(Snapshot.OriginalQuantityScaled, Snapshot.QuantityScale, Snapshot.Unit);
    public string RefundedText => ArabicDisplay.Quantity(Snapshot.RefundedQuantityScaled, Snapshot.QuantityScale, Snapshot.Unit);
    public string RemainingText => ArabicDisplay.Quantity(Snapshot.RemainingRefundableScaled, Snapshot.QuantityScale, Snapshot.Unit);
    public string EffectiveTotalText => ArabicDisplay.Money(Snapshot.EffectiveLineTotalMinor);
}

public sealed class CustomOrderRowViewModel(CustomOrderSnapshot snapshot)
{
    public CustomOrderSnapshot Snapshot { get; } = snapshot;
    public Guid Id => Snapshot.Id;
    public string OrderNumber => Snapshot.OrderNumber;
    public string CustomerName => Snapshot.CustomerName;
    public string Phone => Snapshot.CustomerPhone;
    public string Description => Snapshot.Description;
    public string DueText => Snapshot.DueAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    public string TotalText => ArabicDisplay.Money(Snapshot.TotalMinor);
    public string PaidText => ArabicDisplay.Money(Snapshot.PaidMinor);
    public string RemainingText => ArabicDisplay.Money(Snapshot.RemainingMinor);
    public string CustomerBalanceText => ArabicDisplay.Money(Snapshot.CustomerBalanceMinor);
    public string PaymentStatusText => Snapshot.Status == SugarERP.Domain.CustomOrderStatus.Cancelled ? "ملغي"
        : Snapshot.RemainingMinor <= 0 ? "مدفوع"
        : Snapshot.PaidMinor > 0 ? "مدفوع جزئياً"
        : "غير مدفوع";
    public string LinesSummary => Snapshot.Lines is { Count: > 0 }
        ? string.Join("، ", Snapshot.Lines.Select(value => $"{ArabicDisplay.Quantity(value.QuantityScaled, value.QuantityScale, value.Unit)} {value.ItemName}"))
        : Snapshot.Description;
    public bool HasRemainingBalance => Snapshot.RemainingMinor > 0;
    public string StatusText => Snapshot.Status switch
    {
        SugarERP.Domain.CustomOrderStatus.New => "جديد",
        SugarERP.Domain.CustomOrderStatus.Confirmed => "مؤكد",
        SugarERP.Domain.CustomOrderStatus.Ready => "جاهز",
        SugarERP.Domain.CustomOrderStatus.Delivered => "تم التسليم",
        SugarERP.Domain.CustomOrderStatus.Cancelled => "ملغي",
        _ => string.Empty
    };
}

public sealed class CafeProfileRowViewModel : ObservableObject
{
    private CafeProfileSnapshot _snapshot;

    public CafeProfileRowViewModel(CafeProfileSnapshot snapshot) => _snapshot = snapshot;

    public CafeProfileSnapshot Snapshot => _snapshot;
    public Guid Id => Snapshot.Id;
    public string Name => Snapshot.Name;
    public string Kind => Snapshot.Kind;
    public string Phone => Snapshot.Phone;
    public string Address => Snapshot.Address;
    public string BalanceText => ArabicDisplay.Money(Snapshot.BalanceMinor);
    public string OpenOrdersText => Snapshot.OpenOrderCount == 0 ? "لا توجد طلبات مفتوحة" : $"{Snapshot.OpenOrderCount} طلب مفتوح";

    public void Update(CafeProfileSnapshot snapshot)
    {
        _snapshot = snapshot;
        OnPropertyChanged(nameof(Snapshot));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(Phone));
        OnPropertyChanged(nameof(Address));
        OnPropertyChanged(nameof(BalanceText));
        OnPropertyChanged(nameof(OpenOrdersText));
    }
}

public sealed class CafePriceRowViewModel : ObservableObject
{
    private string _priceText;
    private string _quantityText = "0";

    public CafePriceRowViewModel(CafePriceSnapshot snapshot)
    {
        Snapshot = snapshot;
        _priceText = (snapshot.UnitPriceMinor / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    }

    public CafePriceSnapshot Snapshot { get; }
    public Guid ItemId => Snapshot.ItemId;
    public string Name => Snapshot.ItemName;
    public string Sku => Snapshot.Sku;
    public string Unit => Snapshot.Unit;
    public string PriceText { get => _priceText; set => SetProperty(ref _priceText, value); }
    public string QuantityText { get => _quantityText; set => SetProperty(ref _quantityText, value); }
}

public sealed class CafePaymentRowViewModel(CafePaymentSnapshot snapshot)
{
    public CafePaymentSnapshot Snapshot { get; } = snapshot;
    public string Reference => Snapshot.Reference;
    public string AmountText => ArabicDisplay.Money(Snapshot.AmountMinor);
    public string MethodText => Snapshot.Method == SugarERP.Domain.PaymentMethod.Cash ? "كاش" : "فيزا";
    public string DateText => Snapshot.PaidAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
}
