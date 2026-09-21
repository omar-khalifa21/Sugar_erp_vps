using SugarERP.Domain;

namespace SugarERP.Application;

public sealed record CatalogItemSnapshot(
    Guid Id,
    string Sku,
    string NameAr,
    string Unit,
    int QuantityScale,
    long RetailPriceMinor,
    bool Active,
    long QuantityScaled,
    int StockRevision,
    int Version);

public sealed record OpenShiftSnapshot(
    Guid Id,
    ShiftKind Kind,
    string BusinessDate,
    DateTimeOffset OpenedAtUtc,
    long OpeningCashMinor,
    long SalesMinor,
    int ReceiptCount);

public sealed record BranchSnapshot(
    DeviceConfiguration? Configuration,
    OpenShiftSnapshot? OpenShift,
    IReadOnlyList<CatalogItemSnapshot> Items,
    int PendingOutboxCount,
    DateTimeOffset? OldestPendingAtUtc);

public sealed record SaleCartLine(Guid ItemId, long QuantityScaled);

public sealed record CompleteSaleCommand(
    Guid CommandId,
    IReadOnlyList<SaleCartLine> Lines,
    PaymentMethod PaymentMethod,
    FulfillmentKind Fulfillment,
    long DiscountMinor,
    long TipMinor,
    Guid? UserId = null,
    string? Authorization = null);

public sealed record SaleReceipt(
    Guid Id,
    string ReceiptNumber,
    long SubtotalMinor,
    long DiscountMinor,
    long TipMinor,
    long TotalMinor,
    PaymentMethod PaymentMethod,
    DateTimeOffset OccurredAtUtc,
    bool WasAlreadyCommitted);

public sealed record EnrollmentCommand(
    Uri ApiBaseUrl,
    string EnrollmentToken,
    string DeviceName,
    string KeyThumbprint,
    string AppVersion,
    bool TouchMode,
    DeviceProfile? ExpectedProfile = null);

public sealed record EnrollmentResult(
    Guid SiteId,
    Guid DeviceId,
    DeviceProfile Profile,
    string Credential,
    int StreamEpoch);

public interface IBranchOperations
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<BranchSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    Task<OpenShiftSnapshot> OpenShiftAsync(ShiftKind kind, long openingCashMinor, CancellationToken cancellationToken = default);
    Task<SaleReceipt> CompleteSaleAsync(CompleteSaleCommand command, CancellationToken cancellationToken = default);
    Task SaveEnrollmentAsync(EnrollmentCommand command, EnrollmentResult result, CancellationToken cancellationToken = default);
    Task SetTouchModeAsync(bool enabled, CancellationToken cancellationToken = default);
}

public interface IEnrollmentClient
{
    Task<EnrollmentResult> EnrollAsync(EnrollmentCommand command, CancellationToken cancellationToken = default);
}

public sealed record SyncRunResult(
    int Sent,
    int Acknowledged,
    int Remaining,
    string UserMessage,
    bool Succeeded,
    int Received = 0);

public interface IBranchSyncService
{
    Task<SyncRunResult> PushPendingAsync(CancellationToken cancellationToken = default);
    Task<SyncRunResult> SynchronizeAsync(CancellationToken cancellationToken = default);
    Task<ConnectivityResult> TestConnectionAsync(CancellationToken cancellationToken = default);
}

public sealed record ConnectivityResult(bool HealthAvailable, bool ReadyAvailable, string UserMessage);

public sealed class BusinessRuleException(string code, string userMessage) : Exception(userMessage)
{
    public string Code { get; } = code;
    public string UserMessage { get; } = userMessage;
}
