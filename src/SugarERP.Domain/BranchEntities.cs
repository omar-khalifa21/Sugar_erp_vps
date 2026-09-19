namespace SugarERP.Domain;

public enum DeviceProfile
{
    BranchType1,
    BranchType2,
    Kitchen
}

public enum ShiftKind
{
    Morning,
    Evening
}

public enum ShiftStatus
{
    Open,
    Closing,
    Closed
}

public enum PaymentMethod
{
    Cash,
    Visa
}

public enum FulfillmentKind
{
    Takeaway,
    Table
}

public enum SaleStatus
{
    Posted,
    Reversed
}

public enum StockMovementKind
{
    InitialBalance,
    RetailSale,
    CafeIssue,
    CustomerRestock,
    IncomingReceipt,
    KitchenReturn,
    Waste,
    ApprovedAdjustment
}

public enum OutboxState
{
    Pending,
    Sending,
    Acknowledged,
    Failed
}

public sealed class DeviceConfiguration
{
    public int Id { get; set; } = 1;
    public Guid SiteId { get; set; }
    public Guid DeviceId { get; set; }
    public DeviceProfile Profile { get; set; } = DeviceProfile.BranchType1;
    public string SiteName { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = string.Empty;
    public string DeviceCredential { get; set; } = string.Empty;
    public int StreamEpoch { get; set; } = 1;
    public bool TouchMode { get; set; } = true;
    public DateTimeOffset EnrolledAtUtc { get; set; }
}

public sealed class CatalogItem
{
    public Guid Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public int QuantityScale { get; set; } = 1;
    public long RetailPriceMinor { get; set; }
    public bool Active { get; set; } = true;
    public int Version { get; set; } = 1;
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public StockBalance? StockBalance { get; set; }
}

public sealed class StockBalance
{
    public Guid ItemId { get; set; }
    public long QuantityScaled { get; set; }
    public int Revision { get; set; } = 1;
    public DateTimeOffset AsOfUtc { get; set; }
    public CatalogItem Item { get; set; } = null!;
}

public sealed class Shift
{
    public Guid Id { get; set; }
    public ShiftKind Kind { get; set; }
    public ShiftStatus Status { get; set; } = ShiftStatus.Open;
    public string BusinessDate { get; set; } = string.Empty;
    public DateTimeOffset OpenedAtUtc { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public long OpeningCashMinor { get; set; }
    public long? ExpectedCashMinor { get; set; }
    public long? ActualCashMinor { get; set; }
    public ICollection<Sale> Sales { get; set; } = [];
    public ICollection<ShiftItemSnapshot> OpeningItems { get; set; } = [];
}

public sealed class ShiftItemSnapshot
{
    public Guid ShiftId { get; set; }
    public Guid ItemId { get; set; }
    public string NameSnapshot { get; set; } = string.Empty;
    public string UnitSnapshot { get; set; } = string.Empty;
    public int QuantityScale { get; set; }
    public long OpeningQuantityScaled { get; set; }
    public long IncomingScaled { get; set; }
    public long SoldScaled { get; set; }
    public long CafeIssuedScaled { get; set; }
    public long CustomerRestockScaled { get; set; }
    public long KitchenReturnScaled { get; set; }
    public long WasteScaled { get; set; }
    public long AdjustmentScaled { get; set; }
    public long ExpectedCloseScaled { get; set; }
    public long? ActualCloseScaled { get; set; }
    public long? DiscrepancyScaled { get; set; }
    public Shift Shift { get; set; } = null!;
}

public sealed class Sale
{
    public Guid Id { get; set; }
    public Guid CommandId { get; set; }
    public Guid ShiftId { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public string BusinessDate { get; set; } = string.Empty;
    public PaymentMethod PaymentMethod { get; set; }
    public FulfillmentKind Fulfillment { get; set; }
    public long SubtotalMinor { get; set; }
    public long DiscountMinor { get; set; }
    public long TipMinor { get; set; }
    public long TotalMinor { get; set; }
    public SaleStatus Status { get; set; } = SaleStatus.Posted;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public Shift Shift { get; set; } = null!;
    public ICollection<SaleLine> Lines { get; set; } = [];
    public ICollection<SalePayment> Payments { get; set; } = [];
}

public sealed class SaleLine
{
    public Guid Id { get; set; }
    public Guid SaleId { get; set; }
    public Guid ItemId { get; set; }
    public long QuantityScaled { get; set; }
    public int QuantityScale { get; set; }
    public long UnitPriceMinor { get; set; }
    public long AllocatedDiscountMinor { get; set; }
    public long TotalMinor { get; set; }
    public string NameSnapshot { get; set; } = string.Empty;
    public string SkuSnapshot { get; set; } = string.Empty;
    public string UnitSnapshot { get; set; } = string.Empty;
    public Sale Sale { get; set; } = null!;
}

public sealed class SalePayment
{
    public Guid Id { get; set; }
    public Guid SaleId { get; set; }
    public PaymentMethod Method { get; set; }
    public long AmountMinor { get; set; }
    public DateTimeOffset PaidAtUtc { get; set; }
    public string? Reference { get; set; }
    public Sale Sale { get; set; } = null!;
}

public sealed class StockMovement
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid SourceLineId { get; set; }
    public Guid ItemId { get; set; }
    public Guid? ShiftId { get; set; }
    public StockMovementKind Kind { get; set; }
    public long DeltaScaled { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}

public sealed class CashMovement
{
    public Guid Id { get; set; }
    public Guid ShiftId { get; set; }
    public Guid SourceId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public long AmountMinor { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}

public sealed class OutboxMessage
{
    public Guid EventId { get; set; }
    public Guid AggregateId { get; set; }
    public int DeviceSequence { get; set; }
    public string EventType { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string DependenciesJson { get; set; } = "[]";
    public string ContentHash { get; set; } = string.Empty;
    public OutboxState State { get; set; } = OutboxState.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public DateTimeOffset? AcknowledgedAtUtc { get; set; }
    public string? LastErrorCode { get; set; }
}

public sealed class InboxMessage
{
    public Guid EventId { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public DateTimeOffset AppliedAtUtc { get; set; }
}

public sealed class SequenceState
{
    public int Id { get; set; } = 1;
    public int NextDeviceSequence { get; set; } = 1;
    public int NextReceiptSequence { get; set; } = 1;
    public int NextCustomOrderSequence { get; set; } = 1;
}
