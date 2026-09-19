namespace SugarERP.Domain;

public enum KitchenRequestStatus
{
    Draft,
    Submitted,
    Approved,
    Rejected,
    Partial,
    Fulfilled,
    Closed
}

public enum ShipmentStatus
{
    Dispatched,
    AwaitingReceipt,
    Received,
    Disputed,
    Decided,
    PendingSiteApply,
    Resolved
}

public enum IncomingReceiptStatus
{
    Accepted,
    Disputed,
    Resolved
}

public enum HoldStatus
{
    PendingDecision,
    PendingSiteApply,
    Released
}

public enum KitchenReturnStatus
{
    Dispatched,
    Acknowledged,
    Disputed
}

public enum CorrectionKind
{
    Void,
    Refund,
    Correction
}

public enum StockDisposition
{
    Restock,
    Discard
}

public enum StockCountStatus
{
    Recorded,
    AdjustmentRequested
}

public enum AdjustmentRequestStatus
{
    PendingAdmin,
    Approved,
    Rejected,
    Applied
}

public enum SideEffectKind
{
    PrintReceipt,
    PrintCustomOrder,
    PrintShiftReport,
    ExportShiftReport,
    UploadShiftReport
}

public enum CustomOrderStatus
{
    New,
    Confirmed,
    Ready,
    Delivered,
    Cancelled
}

public sealed class CafeCustomer
{
    public Guid Id { get; set; }
    public Guid CommandId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public ICollection<CafePrice> Prices { get; set; } = [];
    public ICollection<CustomOrder> Orders { get; set; } = [];
}

public sealed class CafePrice
{
    public Guid Id { get; set; }
    public Guid CafeCustomerId { get; set; }
    public Guid ItemId { get; set; }
    public long UnitPriceMinor { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public CafeCustomer Customer { get; set; } = null!;
    public CatalogItem Item { get; set; } = null!;
}

public sealed class CustomOrder
{
    public Guid Id { get; set; }
    public Guid CommandId { get; set; }
    public Guid? CafeCustomerId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset DueAtUtc { get; set; }
    public long TotalMinor { get; set; }
    public CustomOrderStatus Status { get; set; } = CustomOrderStatus.New;
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public ICollection<CustomOrderPayment> Payments { get; set; } = [];
    public ICollection<CustomOrderActivity> Activities { get; set; } = [];
    public ICollection<CustomOrderLine> Lines { get; set; } = [];
    public CafeCustomer? CafeCustomer { get; set; }
}

public sealed class CustomOrderLine
{
    public Guid Id { get; set; }
    public Guid CustomOrderId { get; set; }
    public Guid ItemId { get; set; }
    public string ItemNameSnapshot { get; set; } = string.Empty;
    public string UnitSnapshot { get; set; } = string.Empty;
    public int QuantityScale { get; set; }
    public long QuantityScaled { get; set; }
    public long UnitPriceMinor { get; set; }
    public long LineTotalMinor { get; set; }
    public CustomOrder Order { get; set; } = null!;
}

public sealed class CustomOrderPayment
{
    public Guid Id { get; set; }
    public Guid CommandId { get; set; }
    public Guid CustomOrderId { get; set; }
    public Guid? CafeCustomerId { get; set; }
    public Guid? ShiftId { get; set; }
    public PaymentMethod Method { get; set; }
    public long AmountMinor { get; set; }
    public DateTimeOffset PaidAtUtc { get; set; }
    public CustomOrder Order { get; set; } = null!;
}

public sealed class CustomOrderActivity
{
    public Guid Id { get; set; }
    public Guid CommandId { get; set; }
    public Guid CustomOrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public CustomOrder Order { get; set; } = null!;
}

public enum SideEffectState
{
    Pending,
    Running,
    Completed,
    Failed
}

public sealed class BranchLocalSettings
{
    public int Id { get; set; } = 1;
    public string ExportDirectory { get; set; } = string.Empty;
    public string PrinterName { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class KitchenRequest
{
    public Guid Id { get; set; }
    public Guid CommandId { get; set; }
    public KitchenRequestStatus Status { get; set; } = KitchenRequestStatus.Draft;
    public string BusinessDate { get; set; } = string.Empty;
    public DateTimeOffset RequestedAtUtc { get; set; }
    public DateTimeOffset? SubmittedAtUtc { get; set; }
    public int Version { get; set; } = 1;
    public ICollection<KitchenRequestLine> Lines { get; set; } = [];
}

public sealed class KitchenRequestLine
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public Guid ItemId { get; set; }
    public string NameSnapshot { get; set; } = string.Empty;
    public string UnitSnapshot { get; set; } = string.Empty;
    public int QuantityScale { get; set; }
    public long RequestedScaled { get; set; }
    public long? ApprovedScaled { get; set; }
    public long SentScaled { get; set; }
    public KitchenRequest Request { get; set; } = null!;
}

public sealed class Shipment
{
    public Guid Id { get; set; }
    public Guid CommandId { get; set; }
    public Guid? RequestId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public ShipmentStatus Status { get; set; } = ShipmentStatus.Dispatched;
    public int Version { get; set; } = 1;
    public DateTimeOffset DispatchedAtUtc { get; set; }
    public bool SyntheticDemo { get; set; }
    public ICollection<ShipmentLine> Lines { get; set; } = [];
    public IncomingReceipt? Receipt { get; set; }
}

public sealed class ShipmentLine
{
    public Guid Id { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid? RequestLineId { get; set; }
    public Guid ItemId { get; set; }
    public string NameSnapshot { get; set; } = string.Empty;
    public string UnitSnapshot { get; set; } = string.Empty;
    public int QuantityScale { get; set; }
    public long SentScaled { get; set; }
    public Shipment Shipment { get; set; } = null!;
}

public sealed class IncomingReceipt
{
    public Guid Id { get; set; }
    public Guid CommandId { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid ReceivingShiftId { get; set; }
    public IncomingReceiptStatus Status { get; set; }
    public DateTimeOffset CountedAtUtc { get; set; }
    public DateTimeOffset? AcceptedAtUtc { get; set; }
    public ICollection<IncomingReceiptLine> Lines { get; set; } = [];
    public Shipment Shipment { get; set; } = null!;
}

public sealed class IncomingReceiptLine
{
    public Guid Id { get; set; }
    public Guid ReceiptId { get; set; }
    public Guid ShipmentLineId { get; set; }
    public Guid ItemId { get; set; }
    public long SentScaled { get; set; }
    public long CountedScaled { get; set; }
    public long? ConfirmedScaled { get; set; }
    public IncomingReceipt Receipt { get; set; } = null!;
}

public sealed class StockHold
{
    public Guid Id { get; set; }
    public Guid ReceiptLineId { get; set; }
    public Guid ItemId { get; set; }
    public long PhysicalCountScaled { get; set; }
    public HoldStatus Status { get; set; } = HoldStatus.PendingDecision;
    public long ReleasedScaled { get; set; }
    public Guid? DecisionId { get; set; }
}

public sealed class QuantityConflict
{
    public Guid Id { get; set; }
    public Guid ReceiptId { get; set; }
    public string Status { get; set; } = "PENDING_ADMIN";
    public DateTimeOffset ReportedAtUtc { get; set; }
    public ICollection<ConflictLine> Lines { get; set; } = [];
}

public sealed class ConflictLine
{
    public Guid Id { get; set; }
    public Guid ConflictId { get; set; }
    public Guid ReceiptLineId { get; set; }
    public long SentScaled { get; set; }
    public long CountedScaled { get; set; }
    public string Note { get; set; } = string.Empty;
    public QuantityConflict Conflict { get; set; } = null!;
}

public sealed class KitchenReturn
{
    public Guid Id { get; set; }
    public Guid CommandId { get; set; }
    public Guid ShiftId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public KitchenReturnStatus Status { get; set; } = KitchenReturnStatus.Dispatched;
    public DateTimeOffset DispatchedAtUtc { get; set; }
    public ICollection<KitchenReturnLine> Lines { get; set; } = [];
}

public sealed class KitchenReturnLine
{
    public Guid Id { get; set; }
    public Guid ReturnId { get; set; }
    public Guid ItemId { get; set; }
    public string NameSnapshot { get; set; } = string.Empty;
    public string UnitSnapshot { get; set; } = string.Empty;
    public int QuantityScale { get; set; }
    public long SentScaled { get; set; }
    public KitchenReturn Return { get; set; } = null!;
}

public sealed class SaleCorrection
{
    public Guid Id { get; set; }
    public Guid CommandId { get; set; }
    public Guid OriginalSaleId { get; set; }
    public Guid PostingShiftId { get; set; }
    public CorrectionKind Kind { get; set; } = CorrectionKind.Refund;
    public string Reason { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public long RefundTotalMinor { get; set; }
    public PaymentMethod RefundMethod { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public ICollection<CorrectionLine> Lines { get; set; } = [];
    public RefundPayment? Payment { get; set; }
}

public sealed class CorrectionLine
{
    public Guid Id { get; set; }
    public Guid CorrectionId { get; set; }
    public Guid OriginalSaleLineId { get; set; }
    public Guid ItemId { get; set; }
    public long QuantityDeltaScaled { get; set; }
    public long AmountDeltaMinor { get; set; }
    public long RestockScaled { get; set; }
    public StockDisposition Disposition { get; set; }
    public string NameSnapshot { get; set; } = string.Empty;
    public string UnitSnapshot { get; set; } = string.Empty;
    public int QuantityScale { get; set; }
    public SaleCorrection Correction { get; set; } = null!;
}

public sealed class RefundPayment
{
    public Guid Id { get; set; }
    public Guid CorrectionId { get; set; }
    public PaymentMethod Method { get; set; }
    public long AmountMinor { get; set; }
    public DateTimeOffset PaidAtUtc { get; set; }
    public string? Reference { get; set; }
    public SaleCorrection Correction { get; set; } = null!;
}

public sealed class StockCount
{
    public Guid Id { get; set; }
    public Guid CommandId { get; set; }
    public Guid ShiftId { get; set; }
    public StockCountStatus Status { get; set; }
    public DateTimeOffset CountedAtUtc { get; set; }
    public ICollection<StockCountLine> Lines { get; set; } = [];
}

public sealed class StockCountLine
{
    public Guid Id { get; set; }
    public Guid CountId { get; set; }
    public Guid ItemId { get; set; }
    public string NameSnapshot { get; set; } = string.Empty;
    public string UnitSnapshot { get; set; } = string.Empty;
    public int QuantityScale { get; set; }
    public long ExpectedScaled { get; set; }
    public long ActualScaled { get; set; }
    public long DifferenceScaled { get; set; }
    public StockCount Count { get; set; } = null!;
}

public sealed class AdjustmentRequest
{
    public Guid Id { get; set; }
    public Guid CountLineId { get; set; }
    public long ProposedDeltaScaled { get; set; }
    public string Reason { get; set; } = string.Empty;
    public AdjustmentRequestStatus Status { get; set; } = AdjustmentRequestStatus.PendingAdmin;
    public Guid? AdminDecisionId { get; set; }
    public Guid? AppliedDocumentId { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
}

public sealed class SideEffectJob
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public SideEffectKind Kind { get; set; }
    public int DocumentVersion { get; set; } = 1;
    public SideEffectState State { get; set; } = SideEffectState.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public sealed class ReportArtifact
{
    public Guid Id { get; set; }
    public Guid ShiftId { get; set; }
    public int ReportVersion { get; set; } = 1;
    public string ContentHash { get; set; } = string.Empty;
    public string Format { get; set; } = "xlsx";
    public string LocalPath { get; set; } = string.Empty;
    public long ByteLength { get; set; }
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public DateTimeOffset? UploadedAtUtc { get; set; }
    public string? Error { get; set; }
}

public sealed class SyncCursor
{
    public string FeedScope { get; set; } = "device";
    public string? Cursor { get; set; }
    public string? SnapshotVersion { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class RemoteStockProjection
{
    public Guid SiteId { get; set; }
    public Guid ItemId { get; set; }
    public string SiteName { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public int QuantityScale { get; set; }
    public long QuantityScaled { get; set; }
    public DateTimeOffset AsOfUtc { get; set; }
}
