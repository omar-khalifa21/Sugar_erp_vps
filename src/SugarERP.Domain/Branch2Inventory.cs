namespace SugarERP.Domain;

public enum BranchInventoryLocation { Stock, Display }
public enum Branch2TransactionKind { IncomingReceipt, StockToDisplay, RetailSale, CafeIssue, KitchenReturn, DisplayReturnToStock, ApprovedAdjustment }

public sealed class BranchLocationBalance
{
    public Guid ItemId { get; set; }
    public BranchInventoryLocation Location { get; set; }
    public long QuantityScaled { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class BranchInventoryTransaction
{
    public Guid Id { get; set; }
    public Guid ReferenceId { get; set; }
    public Guid SiteId { get; set; }
    public Guid UserId { get; set; }
    public Branch2TransactionKind Kind { get; set; }
    public string Reason { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public DateTimeOffset OccurredAtUtc { get; set; }
    public List<BranchInventoryTransactionLine> Lines { get; set; } = [];
}

public sealed class BranchInventoryTransactionLine
{
    public Guid Id { get; set; }
    public Guid TransactionId { get; set; }
    public Guid ItemId { get; set; }
    public BranchInventoryLocation Location { get; set; }
    public long DeltaScaled { get; set; }
    public BranchInventoryTransaction Transaction { get; set; } = null!;
}
