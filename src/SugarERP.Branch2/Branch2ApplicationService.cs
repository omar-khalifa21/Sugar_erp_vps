using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SugarERP.Application;
using SugarERP.Domain;
using SugarERP.Infrastructure.Local;
using SugarERP.Sync.Client;

namespace SugarERP.Branch2;

public sealed record LocationStockRow(Guid ItemId, string Name, string Unit, int Scale, long StockScaled, long DisplayScaled)
{
    public decimal Stock => (decimal)StockScaled / Scale;
    public decimal Display => (decimal)DisplayScaled / Scale;
    public decimal Total => Stock + Display;
}
public sealed record Branch2CloseResult(CloseShiftResult Close, string? ReportPath, string? ReportError);

public sealed class Branch2ApplicationService(LocalDatabase database, HttpClient http)
{
    private Guid _userId;
    private string? _authorization;
    private DateTimeOffset _authorizationExpires;
    public LocalDatabase Database => database;
    public bool HasSession => _userId != Guid.Empty && _authorization is not null && DateTimeOffset.UtcNow < _authorizationExpires;
    public async Task EnrollAsync(Uri api, string code, string deviceName)
    {
        await using (var existing = database.CreateContext())
            if (await existing.DeviceConfigurations.AnyAsync()) throw new BusinessRuleException("ALREADY_ENROLLED", "الجهاز مرتبط بالفعل.");
        var command = new EnrollmentCommand(api, code, deviceName, EnrollmentClient.CreateKeyThumbprint(),
            System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0", true, DeviceProfile.BranchType2);
        var result = await new EnrollmentClient(http).EnrollAsync(command);
        if (result.Profile != DeviceProfile.BranchType2) throw new BusinessRuleException("WRONG_PROFILE", "الرمز يجب أن يكون لفرع نوع ٢.");
        await database.WriteLock.WaitAsync();
        try
        {
            await using var db = database.CreateContext();
            if (await db.DeviceConfigurations.AnyAsync()) throw new BusinessRuleException("ALREADY_ENROLLED", "الجهاز مرتبط بالفعل؛ لا يمكن تغيير موقعه محلياً.");
            db.DeviceConfigurations.Add(new DeviceConfiguration { SiteId = result.SiteId, DeviceId = result.DeviceId, Profile = result.Profile,
                SiteName = "فرع نوع ٢", ApiBaseUrl = api.ToString().TrimEnd('/'), DeviceCredential = DeviceCredentialProtector.Protect(result.Credential), StreamEpoch = result.StreamEpoch });
            db.SequenceStates.Add(new SequenceState());
            await db.SaveChangesAsync();
        }
        finally { database.WriteLock.Release(); }
        await BootstrapAsync();
    }
    public async Task LoginAsync(string username, string password)
    {
        var config = await Configuration();
        using var login = await http.PostAsJsonAsync(config.ApiBaseUrl + "/auth/login", new { username, password });
        using var loginBody = await Read(login);
        using var request = new HttpRequestMessage(HttpMethod.Post, config.ApiBaseUrl + "/auth/desktop-authorization");
        request.Headers.Authorization = new("Bearer", loginBody.RootElement.GetProperty("access_token").GetString());
        request.Headers.Add("x-device-id", config.DeviceId.ToString());
        request.Headers.Add("x-device-secret", DeviceCredentialProtector.Unprotect(config.DeviceCredential));
        using var response = await http.SendAsync(request);
        using var body = await Read(response);
        _userId = body.RootElement.GetProperty("user_id").GetGuid();
        _authorization = body.RootElement.GetProperty("authorization").GetString();
        _authorizationExpires = DateTimeOffset.UtcNow.AddSeconds(body.RootElement.GetProperty("expires_in_seconds").GetInt32() - 5);
    }
    public void SignOut() { _userId = Guid.Empty; _authorization = null; }
    public async Task BootstrapAsync()
    {
        var config = await Configuration();
        using var request = new HttpRequestMessage(HttpMethod.Get, config.ApiBaseUrl + "/sync/bootstrap");
        request.Headers.Add("x-device-id", config.DeviceId.ToString());
        request.Headers.Add("x-device-secret", DeviceCredentialProtector.Unprotect(config.DeviceCredential));
        using var response = await http.SendAsync(request);
        using var body = await Read(response);
        if (body.RootElement.GetProperty("site").GetProperty("id").GetGuid() != config.SiteId) throw new IOException("Bootstrap belongs to another site");
        await database.WriteLock.WaitAsync();
        try
        {
            await using var db = database.CreateContext();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var saved = await db.DeviceConfigurations.SingleAsync();
            saved.SiteName = body.RootElement.GetProperty("site").GetProperty("name").GetString()!;
            foreach (var row in body.RootElement.GetProperty("catalog").EnumerateArray())
            {
                var id = row.GetProperty("id").GetGuid(); var item = await db.CatalogItems.FindAsync(id);
                if (item is null) { item = new CatalogItem { Id = id }; db.CatalogItems.Add(item); db.StockBalances.Add(new StockBalance { ItemId = id, Revision = 1 }); }
                var version = row.GetProperty("version").GetInt32(); if (version < item.Version) continue;
                item.Sku = row.GetProperty("sku").GetString()!; item.NameAr = row.GetProperty("nameAr").GetString()!;
                item.Unit = row.GetProperty("unit").GetString()!; item.QuantityScale = row.GetProperty("quantityScale").GetInt32();
                item.RetailPriceMinor = row.GetProperty("retailPriceMinor").GetInt64(); item.Version = version; item.Active = true; item.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
            foreach (var row in body.RootElement.GetProperty("customers").EnumerateArray())
            {
                var id = row.GetProperty("id").GetGuid();
                // Pending local customer edits own their state until acknowledged.
                if (await db.OutboxMessages.AnyAsync(x => x.AggregateId == id && x.State != OutboxState.Acknowledged)) continue;
                var customer = await db.CafeCustomers.Include(x => x.Prices).SingleOrDefaultAsync(x => x.Id == id);
                if (customer is null)
                {
                    customer = new CafeCustomer { Id = id, CommandId = Guid.NewGuid(), CreatedAtUtc = DateTimeOffset.UtcNow };
                    db.CafeCustomers.Add(customer);
                }
                customer.Name = row.GetProperty("name").GetString()!;
                customer.Kind = "Cafe";
                customer.Phone = row.TryGetProperty("contact", out var phone) ? phone.GetString() ?? "" : "";
                customer.Active = true; customer.UpdatedAtUtc = DateTimeOffset.UtcNow;
                foreach (var price in row.GetProperty("prices").EnumerateArray())
                {
                    var itemId = price.GetProperty("itemId").GetGuid();
                    if (!await db.CatalogItems.AnyAsync(x => x.Id == itemId) && !db.CatalogItems.Local.Any(x => x.Id == itemId)) continue;
                    var existing = customer.Prices.SingleOrDefault(x => x.ItemId == itemId);
                    if (existing is null) { existing = new CafePrice { Id = Guid.NewGuid(), ItemId = itemId, CafeCustomerId = id }; customer.Prices.Add(existing); }
                    existing.UnitPriceMinor = price.GetProperty("priceMinor").GetInt64(); existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
                }
            }
            await db.SaveChangesAsync(); await transaction.CommitAsync();
        }
        finally { database.WriteLock.Release(); }
    }
    public async Task<IReadOnlyList<LocationStockRow>> StockAsync()
    {
        await using var db = database.CreateContext();
        var items = await db.CatalogItems.AsNoTracking().Where(x => x.Active).OrderBy(x => x.NameAr).ToListAsync();
        var balances = await db.LocationBalances.AsNoTracking().ToListAsync();
        return items.Select(x => new LocationStockRow(x.Id, x.NameAr, x.Unit, x.QuantityScale,
            balances.SingleOrDefault(b => b.ItemId == x.Id && b.Location == BranchInventoryLocation.Stock)?.QuantityScaled ?? 0,
            balances.SingleOrDefault(b => b.ItemId == x.Id && b.Location == BranchInventoryLocation.Display)?.QuantityScaled ?? 0)).ToArray();
    }
    public Task<BranchInventoryTransaction> MoveAsync(Guid commandId, Guid referenceId, IReadOnlyDictionary<Guid, long> quantities, bool endDay, string reason)
    {
        RequireSession();
        return new Branch2InventoryService(database).ExecuteAsync(new(commandId, referenceId, _userId,
            endDay ? Branch2TransactionKind.DisplayReturnToStock : Branch2TransactionKind.StockToDisplay, quantities, reason, _authorization!));
    }
    public Task<SaleReceipt> SellAsync(Guid commandId, IReadOnlyList<SaleCartLine> lines, PaymentMethod payment, long discount, long tip)
    {
        RequireSession();
        return new BranchOperationsService(database).CompleteSaleAsync(new(commandId, lines, payment, FulfillmentKind.Takeaway, discount, tip, _userId, _authorization));
    }
    public Task<KitchenRequestSnapshot> RequestAsync(Guid commandId, IReadOnlyList<QuantityInput> lines)
    {
        RequireSession(); return new BranchModuleOperationsService(database).CreateKitchenRequestAsync(new(commandId, lines), true);
    }
    public Task<IReadOnlyList<IncomingShipmentSnapshot>> ShipmentsAsync() => new BranchModuleOperationsService(database).GetIncomingShipmentsAsync();
    public Task<IncomingReceiptResult> ReceiveAsync(ReceiveShipmentCommand command)
    {
        RequireSession(); return new BranchModuleOperationsService(database).ReceiveShipmentAsync(command with { UserId = _userId, Authorization = _authorization });
    }
    public Task<OpenShiftSnapshot> OpenShiftAsync(ShiftKind kind, long cash) { RequireSession(); return new BranchOperationsService(database).OpenShiftAsync(kind, cash); }
    public Task<ClosingPreviewSnapshot> ClosingPreviewAsync() { RequireSession(); return new BranchModuleOperationsService(database).GetClosingPreviewAsync(); }
    public async Task<Branch2CloseResult> CloseDayAsync(Guid commandId, IReadOnlyList<ClosingCountInput> counts, long actualCashMinor)
    {
        RequireSession();
        var modules = new BranchModuleOperationsService(database);
        var result = await modules.CloseShiftAsync(new(commandId, counts, actualCashMinor, _userId, _authorization));
        try
        {
            var written = await new OpenXmlShiftReportWriter().WriteAsync(result.Report, new ReportDirectorySettings("Branch2").GetDirectory());
            await modules.MarkReportSucceededAsync(result.ExportJobId, written);
            return new(result, written.Path, null);
        }
        catch (Exception error)
        {
            await modules.MarkReportFailedAsync(result.ExportJobId, error.Message);
            return new(result, null, "أغلقت الوردية وحفظت الأرصدة، لكن فشل إنشاء Excel. يمكن إعادة التقرير لاحقاً من سجل الورديات.");
        }
    }
    public Task<IReadOnlyList<CustomOrderSnapshot>> CafeOrdersAsync() => new BranchModuleOperationsService(database).GetCustomOrdersAsync();
    public Task<CustomOrderSnapshot> CreateCafeOrderAsync(CreateCustomOrderCommand command)
    {
        RequireSession(); return new BranchModuleOperationsService(database).CreateCustomOrderAsync(command);
    }
    public Task<CustomOrderSnapshot> DeliverCafeOrderAsync(Guid commandId, Guid orderId, int version)
    {
        RequireSession(); return new BranchModuleOperationsService(database).ChangeCustomOrderStatusAsync(new(commandId, orderId, version, CustomOrderStatus.Delivered, _userId, _authorization));
    }
    public Task<CustomOrderSnapshot> CollectCafePaymentAsync(Guid commandId, Guid orderId, int version, long amount, PaymentMethod method)
    {
        RequireSession(); return new BranchModuleOperationsService(database).AddCustomOrderPaymentAsync(new(commandId, orderId, version, amount, method));
    }
    public Task<CustomOrderSnapshot> CancelCafeOrderAsync(Guid commandId, Guid orderId, int version)
    {
        RequireSession(); return new BranchModuleOperationsService(database).ChangeCustomOrderStatusAsync(new(commandId, orderId, version, CustomOrderStatus.Cancelled, _userId, _authorization));
    }
    public Task<KitchenReturnSnapshot> ReturnToKitchenAsync(Guid commandId, string reason, IReadOnlyList<QuantityInput> lines)
    {
        RequireSession(); return new BranchModuleOperationsService(database).DispatchKitchenReturnAsync(new(commandId, reason, lines, _userId, _authorization));
    }
    public async Task<SyncRunResult> SyncAsync() { await BootstrapAsync(); return await new BranchSyncService(http, database).SynchronizeAsync(); }
    private void RequireSession() { if (!HasSession) throw new BusinessRuleException("SESSION_EXPIRED", "سجل دخول مستخدم مخول؛ صلاحية التشغيل تنتهي بعد ١٥ دقيقة."); }
    private async Task<DeviceConfiguration> Configuration() { await using var db = database.CreateContext(); return await db.DeviceConfigurations.AsNoTracking().SingleOrDefaultAsync() ?? throw new BusinessRuleException("NOT_ENROLLED", "اربط الجهاز أولاً."); }
    private static async Task<JsonDocument> Read(HttpResponseMessage response)
    {
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (response.IsSuccessStatusCode) return body;
        body.Dispose(); throw new BusinessRuleException(response.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "UNAUTHENTICATED" : "API_ERROR",
            response.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "بيانات الدخول غير صحيحة أو انتهت الصلاحية." : "رفض الخادم العملية. راجع الصلاحيات والبيانات.");
    }
}
