using SugarERP.Domain;

namespace SugarERP.Application;

public sealed record BranchSettingsSnapshot(string ConnectionAddress, string ExportDirectory, string PrinterName, bool TouchMode);
public sealed record SaveBranchSettingsCommand(string ConnectionAddress, string ExportDirectory, string PrinterName, bool TouchMode);

public sealed record CatalogModuleSnapshot(
    IReadOnlyList<CatalogItemSnapshot> Items,
    IReadOnlyList<StockHoldSnapshot> Holds,
    IReadOnlyList<RemoteStockSnapshot> OtherBranches,
    DateTimeOffset? LastSyncAtUtc);

public sealed record StockHoldSnapshot(
    Guid Id,
    Guid ItemId,
    string ItemName,
    string Unit,
    int QuantityScale,
    long PhysicalCountScaled,
    string Status);

public sealed record RemoteStockSnapshot(
    Guid SiteId,
    string SiteName,
    Guid ItemId,
    string ItemName,
    string Unit,
    int QuantityScale,
    long QuantityScaled,
    DateTimeOffset AsOfUtc);

public sealed record QuantityInput(Guid ItemId, long QuantityScaled);
public sealed record SaveCatalogItemCommand(
    Guid CommandId,
    Guid? ItemId,
    int? ExpectedVersion,
    string Sku,
    string NameAr,
    string Unit,
    int QuantityScale,
    long RetailPriceMinor);

public enum RequestDeliveryState
{
    Draft,
    Waiting,
    Sent,
    Failed,
    Received
}

public sealed record CreateKitchenRequestCommand(Guid CommandId, IReadOnlyList<QuantityInput> Lines);
public sealed record KitchenRequestLineSnapshot(
    Guid Id,
    Guid ItemId,
    string ItemName,
    string Unit,
    int QuantityScale,
    long RequestedScaled,
    long? ApprovedScaled,
    long SentScaled);
public sealed record KitchenRequestSnapshot(
    Guid Id,
    KitchenRequestStatus Status,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? SubmittedAtUtc,
    int Version,
    IReadOnlyList<KitchenRequestLineSnapshot> Lines,
    RequestDeliveryState DeliveryState,
    bool WasAlreadyCommitted = false);

public sealed record ShipmentLineSnapshot(
    Guid Id,
    Guid ItemId,
    string ItemName,
    string Unit,
    int QuantityScale,
    long SentScaled,
    long? CountedScaled);
public sealed record IncomingShipmentSnapshot(
    Guid Id,
    string Reference,
    ShipmentStatus Status,
    int Version,
    DateTimeOffset DispatchedAtUtc,
    bool SyntheticDemo,
    IReadOnlyList<ShipmentLineSnapshot> Lines,
    Guid? ReceiptId,
    bool IsReceivable);
public sealed record ShipmentCountInput(Guid ShipmentLineId, long CountedScaled);
public sealed record ReceiveShipmentCommand(Guid CommandId, Guid ShipmentId, int ExpectedVersion, IReadOnlyList<ShipmentCountInput> Lines, Guid? UserId = null, string? Authorization = null);
public sealed record IncomingReceiptResult(
    Guid ReceiptId,
    Guid ShipmentId,
    IncomingReceiptStatus Status,
    bool EntireShipmentHeld,
    bool WasAlreadyCommitted);
public sealed record PostManualIncomingCommand(
    Guid CommandId,
    string Reason,
    IReadOnlyList<QuantityInput> Lines,
    Guid? UserId = null,
    string? Authorization = null);
public sealed record ManualIncomingResult(Guid DocumentId, bool WasAlreadyCommitted);

public sealed record DispatchKitchenReturnCommand(Guid CommandId, string Reason, IReadOnlyList<QuantityInput> Lines, Guid? UserId = null, string? Authorization = null);
public sealed record KitchenReturnSnapshot(
    Guid Id,
    string Reference,
    KitchenReturnStatus Status,
    DateTimeOffset DispatchedAtUtc,
    IReadOnlyList<QuantityInput> Lines,
    bool WasAlreadyCommitted = false);

public sealed record SaleLineDetails(
    Guid Id,
    Guid ItemId,
    string ItemName,
    string Sku,
    string Unit,
    int QuantityScale,
    long OriginalQuantityScaled,
    long RefundedQuantityScaled,
    long RemainingRefundableScaled,
    long UnitPriceMinor,
    long AllocatedDiscountMinor,
    long EffectiveLineTotalMinor);
public sealed record SaleDetailsSnapshot(
    Guid Id,
    Guid ShiftId,
    ShiftKind ShiftKind,
    string ReceiptNumber,
    DateTimeOffset OccurredAtUtc,
    PaymentMethod PaymentMethod,
    FulfillmentKind Fulfillment,
    long SubtotalMinor,
    long DiscountMinor,
    long OriginalTotalMinor,
    long RefundedTotalMinor,
    long EffectiveTotalMinor,
    long TipMinor,
    IReadOnlyList<SaleLineDetails> Lines);
public sealed record SaleListItemSnapshot(
    Guid Id,
    Guid ShiftId,
    string ReceiptNumber,
    DateTimeOffset OccurredAtUtc,
    PaymentMethod PaymentMethod,
    FulfillmentKind Fulfillment,
    long OriginalTotalMinor,
    long RefundedTotalMinor,
    long EffectiveTotalMinor);
public sealed record CorrectSaleCommand(
    Guid CommandId,
    Guid SaleId,
    Guid SaleLineId,
    long QuantityScaled,
    bool Restock,
    string Reason,
    PaymentMethod RefundMethod,
    string Actor);
public sealed record SaleCorrectionResult(Guid CorrectionId, long RefundMinor, bool Restocked, bool WasAlreadyCommitted);

public sealed record CreateCafeProfileCommand(
    Guid CommandId,
    string Name,
    string Kind,
    string Phone,
    string Address,
    Guid? CopyPricesFromCafeId);
public sealed record CafeProfileSnapshot(
    Guid Id,
    string Name,
    string Kind,
    string Phone,
    string Address,
    long BalanceMinor,
    int OpenOrderCount,
    int Version);
public sealed record CafePriceSnapshot(
    Guid ItemId,
    string Sku,
    string ItemName,
    string Unit,
    int QuantityScale,
    long UnitPriceMinor);
public sealed record SaveCafePriceInput(Guid ItemId, long UnitPriceMinor);
public sealed record SaveCafePriceListCommand(Guid CommandId, Guid CafeCustomerId, int ExpectedVersion, IReadOnlyList<SaveCafePriceInput> Prices);
public sealed record CafePaymentSnapshot(Guid Id, string Reference, long AmountMinor, PaymentMethod Method, DateTimeOffset PaidAtUtc);
public sealed record CafeProfileDetailsSnapshot(
    CafeProfileSnapshot Profile,
    IReadOnlyList<CafePriceSnapshot> Prices,
    IReadOnlyList<CustomOrderSnapshot> Orders,
    IReadOnlyList<CafePaymentSnapshot> Payments);
public sealed record CafeOrderLineInput(Guid ItemId, long QuantityScaled);
public sealed record CreateCustomOrderCommand(
    Guid CommandId,
    Guid CafeCustomerId,
    string Description,
    DateTimeOffset DueAtUtc,
    IReadOnlyList<CafeOrderLineInput> Lines);
public sealed record AddCustomOrderPaymentCommand(
    Guid CommandId,
    Guid CustomOrderId,
    int ExpectedVersion,
    long AmountMinor,
    PaymentMethod PaymentMethod);
public sealed record ChangeCustomOrderStatusCommand(
    Guid CommandId,
    Guid CustomOrderId,
    int ExpectedVersion,
    CustomOrderStatus Status,
    Guid? UserId = null,
    string? Authorization = null);
public sealed record CustomOrderSnapshot(
    Guid Id,
    Guid? CafeCustomerId,
    string OrderNumber,
    string CustomerName,
    string CustomerPhone,
    string Description,
    DateTimeOffset DueAtUtc,
    long TotalMinor,
    long PaidMinor,
    long RemainingMinor,
    long CustomerBalanceMinor,
    CustomOrderStatus Status,
    int Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    bool WasAlreadyCommitted = false,
    IReadOnlyList<CustomOrderLineSnapshot>? Lines = null);
public sealed record CustomOrderLineSnapshot(
    Guid ItemId,
    string ItemName,
    string Unit,
    int QuantityScale,
    long QuantityScaled,
    long UnitPriceMinor,
    long LineTotalMinor);

public sealed record ClosingItemSnapshot(
    Guid ItemId,
    string ItemName,
    string Unit,
    int QuantityScale,
    long OpeningScaled,
    long IncomingScaled,
    long SoldScaled,
    long CafeIssuedScaled,
    long CustomerRestockScaled,
    long KitchenReturnScaled,
    long WasteScaled,
    long AdjustmentScaled,
    long ExpectedScaled);
public sealed record ClosingPreviewSnapshot(
    Guid ShiftId,
    ShiftKind Kind,
    string BusinessDate,
    DateTimeOffset OpenedAtUtc,
    long OpeningCashMinor,
    long CashSalesMinor,
    long VisaSalesMinor,
    long CashRefundsMinor,
    long VisaRefundsMinor,
    long ExpectedCashMinor,
    int ReceiptCount,
    int PendingHoldCount,
    int PendingReturnCount,
    IReadOnlyList<ClosingItemSnapshot> Items);
public sealed record ClosingCountInput(Guid ItemId, long ActualScaled);
public sealed record CloseShiftCommand(Guid CommandId, IReadOnlyList<ClosingCountInput> Counts, long ActualCashMinor,
    Guid? UserId = null, string? Authorization = null);
public sealed record CloseShiftResult(
    Guid ShiftId,
    Guid ExportJobId,
    Guid PrintJobId,
    ShiftReportData Report,
    bool WasAlreadyCommitted);

public sealed record ClosedShiftSnapshot(
    Guid Id,
    ShiftKind Kind,
    string BusinessDate,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset ClosedAtUtc,
    long SalesMinor,
    long RefundsMinor,
    int ReceiptCount,
    long ExpectedCashMinor,
    long ActualCashMinor,
    string ReportStatus,
    string? ReportPath,
    string? ReportHash);

public sealed record ShiftReportItemData(
    string Name,
    string Unit,
    int QuantityScale,
    long OpeningScaled,
    long IncomingScaled,
    long SoldScaled,
    long CafeIssuedScaled,
    long CustomerRestockScaled,
    long KitchenReturnScaled,
    long WasteScaled,
    long AdjustmentScaled,
    long ExpectedScaled,
    long ActualScaled,
    long DifferenceScaled);
public sealed record ShiftReportData(
    Guid ShiftId,
    int ReportVersion,
    string SiteName,
    ShiftKind Kind,
    string BusinessDate,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset ClosedAtUtc,
    long OpeningCashMinor,
    long CashSalesMinor,
    long VisaSalesMinor,
    long CashRefundsMinor,
    long VisaRefundsMinor,
    long ExpectedCashMinor,
    long ActualCashMinor,
    long DifferenceMinor,
    int ReceiptCount,
    int PendingHoldCount,
    int PendingReturnCount,
    IReadOnlyList<ShiftReportItemData> Items);
public sealed record ShiftReportWriteResult(string Path, string Sha256, long ByteLength, bool ExistingIdenticalFile);

public interface IShiftReportWriter
{
    Task<ShiftReportWriteResult> WriteAsync(ShiftReportData report, string exportDirectory, CancellationToken cancellationToken = default);
}

public interface IBranchPrinter
{
    IReadOnlyList<string> GetInstalledPrinterNames();
    Task PrintTestAsync(string printerName, string siteName, CancellationToken cancellationToken = default);
    Task PrintSaleAsync(string printerName, string siteName, SaleDetailsSnapshot sale, CancellationToken cancellationToken = default);
    Task PrintCustomOrderAsync(string printerName, string siteName, CustomOrderSnapshot order, CancellationToken cancellationToken = default);
    Task PrintShiftReportAsync(string printerName, ShiftReportData report, CancellationToken cancellationToken = default);
}

public interface ICatalogOperations
{
    Task<CatalogModuleSnapshot> GetCatalogAsync(CancellationToken cancellationToken = default);
    Task<CatalogItemSnapshot> SaveCatalogItemAsync(SaveCatalogItemCommand command, CancellationToken cancellationToken = default);
    Task<CatalogItemSnapshot> ArchiveCatalogItemAsync(Guid commandId, Guid itemId, int expectedVersion, CancellationToken cancellationToken = default);
    Task DeleteCatalogItemAsync(Guid commandId, Guid itemId, int expectedVersion, CancellationToken cancellationToken = default);
}

public interface IBranchLogisticsOperations
{
    Task<IReadOnlyList<KitchenRequestSnapshot>> GetKitchenRequestsAsync(CancellationToken cancellationToken = default);
    Task<KitchenRequestSnapshot> CreateKitchenRequestAsync(CreateKitchenRequestCommand command, bool submit, CancellationToken cancellationToken = default);
    Task<KitchenRequestSnapshot> SubmitKitchenRequestAsync(Guid requestId, int expectedVersion, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IncomingShipmentSnapshot>> GetIncomingShipmentsAsync(CancellationToken cancellationToken = default);
    Task<IncomingShipmentSnapshot> CreateSyntheticDemoShipmentAsync(Guid commandId, CancellationToken cancellationToken = default);
    Task<IncomingReceiptResult> ReceiveShipmentAsync(ReceiveShipmentCommand command, CancellationToken cancellationToken = default);
    Task<ManualIncomingResult> PostManualIncomingAsync(PostManualIncomingCommand command, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<KitchenReturnSnapshot>> GetKitchenReturnsAsync(CancellationToken cancellationToken = default);
    Task<KitchenReturnSnapshot> DispatchKitchenReturnAsync(DispatchKitchenReturnCommand command, CancellationToken cancellationToken = default);
}

public interface IBranchSaleHistoryOperations
{
    Task<IReadOnlyList<SaleListItemSnapshot>> GetCurrentShiftSalesAsync(CancellationToken cancellationToken = default);
    Task<SaleDetailsSnapshot> GetSaleAsync(Guid saleId, CancellationToken cancellationToken = default);
    Task<SaleCorrectionResult> CorrectSaleAsync(CorrectSaleCommand command, CancellationToken cancellationToken = default);
}

public interface ICafeOrderOperations
{
    Task<IReadOnlyList<CustomOrderSnapshot>> GetCustomOrdersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CafeProfileSnapshot>> GetCafeProfilesAsync(CancellationToken cancellationToken = default);
    Task<CafeProfileDetailsSnapshot> GetCafeProfileAsync(Guid cafeCustomerId, CancellationToken cancellationToken = default);
    Task<CafeProfileSnapshot> CreateCafeProfileAsync(CreateCafeProfileCommand command, CancellationToken cancellationToken = default);
    Task ArchiveCafeProfileAsync(Guid commandId, Guid cafeCustomerId, int expectedVersion, CancellationToken cancellationToken = default);
    Task<CafeProfileDetailsSnapshot> SaveCafePriceListAsync(SaveCafePriceListCommand command, CancellationToken cancellationToken = default);
    Task<CustomOrderSnapshot> CreateCustomOrderAsync(CreateCustomOrderCommand command, CancellationToken cancellationToken = default);
    Task<CustomOrderSnapshot> AddCustomOrderPaymentAsync(AddCustomOrderPaymentCommand command, CancellationToken cancellationToken = default);
    Task<CustomOrderSnapshot> ChangeCustomOrderStatusAsync(ChangeCustomOrderStatusCommand command, CancellationToken cancellationToken = default);
}

public interface IShiftClosingOperations
{
    Task<ClosingPreviewSnapshot> GetClosingPreviewAsync(CancellationToken cancellationToken = default);
    Task<CloseShiftResult> CloseShiftAsync(CloseShiftCommand command, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClosedShiftSnapshot>> GetClosedShiftsAsync(CancellationToken cancellationToken = default);
    Task<ShiftReportData> GetShiftReportDataAsync(Guid shiftId, CancellationToken cancellationToken = default);
}

public interface IBranchSideEffectOperations
{
    Task<Guid> GetOrResetExportJobAsync(Guid shiftId, CancellationToken cancellationToken = default);
    Task<Guid> GetOrResetPrintJobAsync(Guid sourceId, SideEffectKind kind, CancellationToken cancellationToken = default);
    Task MarkReportSucceededAsync(Guid exportJobId, ShiftReportWriteResult result, CancellationToken cancellationToken = default);
    Task MarkReportFailedAsync(Guid exportJobId, string error, CancellationToken cancellationToken = default);
    Task MarkPrintSucceededAsync(Guid printJobId, CancellationToken cancellationToken = default);
    Task MarkPrintFailedAsync(Guid printJobId, string error, CancellationToken cancellationToken = default);
    Task<BranchSettingsSnapshot> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(SaveBranchSettingsCommand command, CancellationToken cancellationToken = default);
}

public interface IBranchModuleOperations : ICatalogOperations, IBranchLogisticsOperations,
    IBranchSaleHistoryOperations, ICafeOrderOperations, IShiftClosingOperations, IBranchSideEffectOperations
{
}
