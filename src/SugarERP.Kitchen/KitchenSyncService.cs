using System.Text.Json;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SugarERP.Application;
using SugarERP.Domain;
using SugarERP.Infrastructure.Local;
using SugarERP.Sync.Client;

namespace SugarERP.Kitchen;

public sealed class KitchenSyncService(HttpClient http, KitchenStore store)
{
    private readonly SemaphoreSlim _syncGate = new(1, 1);

    public async Task EnrollAsync(Uri api, string token, string deviceName)
    {
        await using (var existing = store.Open())
            if (await existing.Configuration.AnyAsync()) throw new BusinessRuleException("ALREADY_ENROLLED", "الجهاز مرتبط بالفعل؛ لا تغير هويته محلياً.");
        var result = await new EnrollmentClient(http).EnrollAsync(new EnrollmentCommand(api, token, deviceName,
            EnrollmentClient.CreateKeyThumbprint(), System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0", false, DeviceProfile.Kitchen));
        if (result.Profile != DeviceProfile.Kitchen) throw new InvalidOperationException("رمز التسجيل ليس لموقع مطبخ.");
        await store.WriteLock.WaitAsync();
        try
        {
            await using var db = store.Open();
            if (await db.Configuration.AnyAsync()) throw new BusinessRuleException("ALREADY_ENROLLED", "الجهاز مرتبط بالفعل.");
            db.Configuration.Add(new KitchenConfiguration { ApiBaseUrl = api.ToString().TrimEnd('/'), SiteId = result.SiteId,
                DeviceId = result.DeviceId, ProtectedCredential = DeviceCredentialProtector.Protect(result.Credential), StreamEpoch = result.StreamEpoch });
            await db.SaveChangesAsync();
        }
        finally { store.WriteLock.Release(); }
    }

    public async Task<int> PullAsync(CancellationToken cancellationToken = default, bool forceRetry = false)
    {
        await _syncGate.WaitAsync(cancellationToken);
        try
        {
            Trace.WriteLine("[SYNC] Kitchen item/request sync started.");
            await RecoverMissingRequestAcknowledgementsAsync(cancellationToken);
            await FlushAsync(cancellationToken, forceRetry);
            await BootstrapAsync(cancellationToken);
            var total = 0;
            while (true)
            {
                var result = await PullPageAsync(cancellationToken);
                total += result.Applied;
                if (!result.HasMore) break;
            }
            // Applying a branch request queues a durable receipt acknowledgement.
            // Send it in the same automatic cycle so the branch does not remain at
            // "sent" until the next polling interval.
            await FlushAsync(cancellationToken, forceRetry);
            await using (var state = store.Open())
            {
                var blocked = await state.Outbox.AsNoTracking().OrderBy(value => value.Sequence)
                    .FirstOrDefaultAsync(value => !value.Acknowledged, cancellationToken);
                if (blocked is not null)
                {
                    var detail = blocked.PermanentlyFailed
                        ? $"تعذر إرسال عملية محفوظة ({blocked.LastErrorCode ?? "SYNC_REJECTED"}). راجع الإدارة؛ لم تُحذف العملية."
                        : "توجد عمليات محفوظة بانتظار الاتصال بالخادم، وستتم إعادة إرسالها تلقائياً.";
                    throw new InvalidOperationException(detail);
                }
            }
            Trace.WriteLine($"[SYNC] Kitchen sync completed; applied={total}.");
            return total;
        }
        finally { _syncGate.Release(); }
    }
    private async Task BootstrapAsync(CancellationToken cancellationToken)
    {
        await using var read = store.Open();
        var configuration = await read.Configuration.AsNoTracking().SingleAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, configuration.ApiBaseUrl + "/sync/bootstrap");
        request.Headers.Add("x-device-id", configuration.DeviceId.ToString()); request.Headers.Add("x-device-secret", DeviceCredentialProtector.Unprotect(configuration.ProtectedCredential));
        using var response = await http.SendAsync(request, cancellationToken); response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var catalog = document.RootElement.GetProperty("catalog").EnumerateArray().ToDictionary(x => x.GetProperty("id").GetGuid());
        await store.WriteLock.WaitAsync();
        try
        {
            await using var db = store.Open(); await using var transaction = await db.Database.BeginTransactionAsync();
            var currentConfiguration = await db.Configuration.SingleAsync();
            currentConfiguration.SiteName = document.RootElement.GetProperty("site").GetProperty("name").GetString() ?? "المطبخ";
            foreach (var item in catalog.Values)
            {
                var id = item.GetProperty("id").GetGuid(); var product = await db.Products.FindAsync(id);
                if (product is null) { product = new KitchenProduct { Id = id }; db.Products.Add(product); }
                var version = item.GetProperty("version").GetInt32();
                if (version < product.Version) continue;
                product.Sku = item.GetProperty("sku").GetString()!;
                product.Name = item.GetProperty("nameAr").GetString()!; product.Unit = item.GetProperty("unit").GetString()!;
                product.Kind = item.GetProperty("kind").GetString() ?? "PRODUCT";
                product.QuantityScale = item.GetProperty("quantityScale").GetInt32();
                if (product.Kind == "PRODUCT") product.BasePriceMinor = item.GetProperty("retailPriceMinor").GetInt64();
                product.Active = item.GetProperty("active").GetBoolean(); product.Version = version; product.UpdatedAtUtc = DateTimeOffset.UtcNow;
                if (product.Kind == "INGREDIENT" && await db.Ingredients.FindAsync(id) is null)
                    db.Ingredients.Add(new KitchenIngredientBalance { ItemId = id, Name = product.Name, Unit = product.Unit, QuantityScale = product.QuantityScale, Version = version });
            }
            foreach (var row in document.RootElement.GetProperty("customers").EnumerateArray())
            {
                var id = row.GetProperty("id").GetGuid();
                if (await db.Outbox.AnyAsync(x => x.RequestId == id && !x.Acknowledged && x.UploadJson.Contains("cafe_customer."))) continue;
                var customer = await db.CafeCustomers.Include(x => x.Prices).SingleOrDefaultAsync(x => x.Id == id);
                if (customer is null) { customer = new KitchenCafeCustomer { Id = id }; db.CafeCustomers.Add(customer); }
                customer.Name = row.GetProperty("name").GetString()!; customer.Contact = row.TryGetProperty("contact", out var contact) ? contact.GetString() ?? "" : "";
                customer.Notes = row.TryGetProperty("notes", out var notes) ? notes.GetString() ?? "" : "";
                customer.Active = true; customer.Version = row.TryGetProperty("version", out var cafeVersion) ? cafeVersion.GetInt32() : 1;
                db.CafePrices.RemoveRange(customer.Prices); customer.Prices.Clear();
                foreach (var price in row.GetProperty("prices").EnumerateArray()) customer.Prices.Add(new KitchenCafePrice {
                    CustomerId = id, ItemId = price.GetProperty("itemId").GetGuid(), UnitPriceMinor = price.GetProperty("priceMinor").GetInt64(), Version = price.GetProperty("version").GetInt32() });
            }
            foreach (var row in document.RootElement.GetProperty("recipes").EnumerateArray())
            {
                var productId = row.GetProperty("product_item_id").GetGuid(); var recipe = await db.Recipes.Include(x => x.Components).SingleOrDefaultAsync(x => x.ProductItemId == productId);
                if (recipe is null) { recipe = new KitchenRecipeRecord { ProductItemId = productId }; db.Recipes.Add(recipe); }
                var version = row.GetProperty("version").GetInt32(); if (version <= recipe.Version) continue;
                recipe.OutputScaled = ParseInteger(row.GetProperty("output_scaled")); recipe.Version = version; db.RecipeComponents.RemoveRange(recipe.Components); recipe.Components.Clear();
                foreach (var component in row.GetProperty("components").EnumerateArray()) recipe.Components.Add(new KitchenRecipeComponentRecord { Id = Guid.NewGuid(), ProductItemId = productId,
                    IngredientItemId = component.GetProperty("ingredient_item_id").GetGuid(), QuantityScaled = ParseInteger(component.GetProperty("quantity_scaled")) });
            }
            if (!await db.Outbox.AnyAsync(x => !x.Acknowledged)) foreach (var row in document.RootElement.GetProperty("stock").EnumerateArray().Where(x => x.GetProperty("location").GetString() == "KITCHEN"))
            {
                var itemId = row.GetProperty("itemId").GetGuid(); if (!catalog.TryGetValue(itemId, out var item) || item.GetProperty("kind").GetString() != "INGREDIENT") continue;
                var balance = await db.Ingredients.FindAsync(itemId); if (balance is null) { balance = new KitchenIngredientBalance { ItemId = itemId }; db.Ingredients.Add(balance); }
                balance.Name = item.GetProperty("nameAr").GetString()!; balance.Unit = item.GetProperty("unit").GetString()!; balance.QuantityScale = item.GetProperty("quantityScale").GetInt32();
                balance.QuantityScaled = ParseInteger(row.GetProperty("quantityScaled"), true); balance.Version = row.GetProperty("version").GetInt32();
                balance.InventoryCostMinor = row.TryGetProperty("inventoryCostMinor", out var inventoryCost)
                    ? ParseInteger(inventoryCost, true) : balance.InventoryCostMinor;
            }
            await db.SaveChangesAsync(); await transaction.CommitAsync();
        }
        finally { store.WriteLock.Release(); }
    }
    private static long ParseInteger(JsonElement value, bool allowZero = false) { var parsed = value.ValueKind == JsonValueKind.String ? long.Parse(value.GetString()!, System.Globalization.CultureInfo.InvariantCulture) : value.GetInt64(); if (parsed < 0 || (!allowZero && parsed == 0)) throw new IOException("Invalid central quantity"); return parsed; }
    private static long CostForUse(long inventoryCostMinor, long availableScaled, long usedScaled) =>
        availableScaled <= 0 || inventoryCostMinor <= 0 ? 0 :
        usedScaled >= availableScaled ? inventoryCostMinor :
        checked((long)decimal.Round((decimal)inventoryCostMinor * usedScaled / availableScaled, 0, MidpointRounding.AwayFromZero));
    private async Task<(int Applied, bool HasMore)> PullPageAsync(CancellationToken cancellationToken)
    {
        await using var read = store.Open();
        var configuration = await read.Configuration.AsNoTracking().SingleAsync();
        var connection = Connect(configuration);
        var page = await new CentralApiClient(http).PullAsync(connection, configuration.Cursor, 100, cancellationToken);
        await store.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = store.Open();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var applied = 0;
            foreach (var incoming in page.Events.OrderBy(x => long.Parse(x.ServerPosition)))
            {
                var computedHash = ContractEventFactory.ComputeHash(incoming.Id, incoming.DeviceSequence, incoming.EventType,
                    incoming.SchemaVersion, incoming.OccurredAt, incoming.Payload, incoming.Dependencies);
                if (!string.Equals(computedHash, incoming.ContentHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("فشل التحقق من سلامة حدث مزامنة نازل من الخادم.");
                var seen = await db.Inbox.FindAsync(incoming.Id);
                if (seen is not null) { if (!seen.ContentHash.Equals(incoming.ContentHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("تعارض حدث مزامنة."); continue; }
                if (incoming.EventType == "kitchen_request.submitted") ApplyRequest(db, incoming);
                if (incoming.EventType is "incoming_receipt.accepted" or "incoming_receipt.disputed") ApplyReceipt(db, incoming);
                if (incoming.EventType == "kitchen_return.dispatched") ApplyReturn(db, incoming);
                if (incoming.EventType is "catalog.item_published" or "catalog.item.updated")
                    await ApplyCatalogItemAsync(db, incoming.Payload, (await db.Configuration.SingleAsync(cancellationToken)).SiteId);
                db.Inbox.Add(new KitchenInbox { EventId = incoming.Id, ContentHash = incoming.ContentHash, AppliedAtUtc = DateTimeOffset.UtcNow });
                applied++;
            }
            var current = await db.Configuration.SingleAsync();
            current.Cursor = page.Cursor;
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            await new CentralApiClient(http).AcknowledgeAsync(connection, page.Cursor, cancellationToken);
            if (page.HasMore && page.Cursor == configuration.Cursor) throw new IOException("Sync cursor did not advance");
            return (applied, page.HasMore);
        }
        finally { store.WriteLock.Release(); }
    }

    public async Task DispatchAsync(Guid requestId, IReadOnlyDictionary<Guid, long> quantities)
    {
        await store.WriteLock.WaitAsync();
        try
        {
            await using var db = store.Open();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var configuration = await db.Configuration.SingleAsync();
            var request = await db.Requests.Include(x => x.Lines).SingleAsync(x => x.Id == requestId);
            if (request.Status is "FULFILLED" or "SENT" or "REJECTED") throw new InvalidOperationException("الطلب مغلق.");
            var selected = request.Lines.Where(x => quantities.GetValueOrDefault(x.Id) > 0).ToArray();
            if (selected.Length == 0 || selected.Any(x => quantities[x.Id] > x.RequestedScaled - x.SentScaled)) throw new InvalidOperationException("كمية الإرسال غير صالحة.");
            var recipes = await db.Recipes.Include(x => x.Components).Where(x => selected.Select(line => line.ItemId).Contains(x.ProductItemId)).ToDictionaryAsync(x => x.ProductItemId);
            if (recipes.Count != selected.Select(x => x.ItemId).Distinct().Count()) throw new BusinessRuleException("RECIPE_REQUIRED", "لا يمكن الإرسال: توجد أصناف بلا وصفة فعالة منشورة من الإدارة.");
            var ingredientUse = new Dictionary<Guid, long>();
            var recipeSnapshot = selected.Select(line => {
                var recipe = recipes[line.ItemId]; var output = quantities[line.Id];
                var components = recipe.Components.Select(component => { var numerator = checked(output * component.QuantityScaled); if (numerator % recipe.OutputScaled != 0) throw new BusinessRuleException("RECIPE_ROUNDING", "كمية الإنتاج لا تطابق وحدات الوصفة؛ عدّل الكمية."); var used = numerator / recipe.OutputScaled; ingredientUse[component.IngredientItemId] = checked(ingredientUse.GetValueOrDefault(component.IngredientItemId) + used); return new { ingredient_item_id = component.IngredientItemId, quantity_scaled = used.ToString() }; }).ToArray();
                return new { product_item_id = line.ItemId, output_scaled = output.ToString(), recipe_version = recipe.Version, components };
            }).ToArray();
            var balances = await db.Ingredients.Where(x => ingredientUse.Keys.Contains(x.ItemId)).ToDictionaryAsync(x => x.ItemId);
            if (balances.Count != ingredientUse.Count || ingredientUse.Any(x => balances[x.Key].QuantityScaled < x.Value)) throw new BusinessRuleException("INSUFFICIENT_INGREDIENTS", "الخامات لا تكفي لإرسال هذه الشحنة.");
            var ingredientCosts = ingredientUse.ToDictionary(x => x.Key,
                x => CostForUse(balances[x.Key].InventoryCostMinor, balances[x.Key].QuantityScaled, x.Value));
            var shipmentId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var payload = new {
                shipment_id = shipmentId, request_id = request.Id, destination_site_id = request.BranchSiteId,
                reference = $"K-{now:yyyyMMdd}-{configuration.NextDeviceSequence:000000}", version = 1, finalized = true, dispatched_at = now.ToString("O"),
                lines = selected.Select(x => new { line_id = Guid.NewGuid(), request_line_id = x.Id, item_id = x.ItemId,
                    sent_scaled = quantities[x.Id], quantity_scale = x.QuantityScale, name_snapshot = x.Name, unit_snapshot = x.Unit }).ToArray(),
                recipe_snapshot = recipeSnapshot,
                production_cost_minor = ingredientCosts.Values.Sum().ToString(),
                ingredient_lines = ingredientUse.OrderBy(x => x.Key).Select(x => new {
                    line_id = Guid.NewGuid(), ingredient_item_id = x.Key, quantity_scaled = x.Value.ToString(),
                    cost_minor = ingredientCosts[x.Key].ToString() }).ToArray()
            };
            var contractEvent = ContractEventFactory.Create(shipmentId, configuration.NextDeviceSequence, "shipment.dispatched", now, payload);
            using var document = JsonDocument.Parse(contractEvent.PayloadJson);
            var upload = new SyncUploadEvent(contractEvent.Id, contractEvent.DeviceSequence, contractEvent.EventType, contractEvent.SchemaVersion,
                contractEvent.OccurredAtUtc.ToString("O"), document.RootElement.Clone(), [], contractEvent.ContentHash);
            var queued = new KitchenOutbox { EventId = upload.Id, RequestId = requestId, Sequence = upload.DeviceSequence, UploadJson = JsonSerializer.Serialize(upload), AppliedLocally = true, NextAttemptAtUtc = now };
            db.Outbox.Add(queued);
            foreach (var line in selected) line.SentScaled = checked(line.SentScaled + quantities[line.Id]);
            foreach (var use in ingredientUse)
            {
                var balance = balances[use.Key]; balance.QuantityScaled -= use.Value;
                balance.InventoryCostMinor -= ingredientCosts[use.Key]; balance.Version++;
                var movementLine = upload.Payload.GetProperty("ingredient_lines").EnumerateArray()
                    .Single(x => x.GetProperty("ingredient_item_id").GetGuid() == use.Key);
                db.IngredientMovements.Add(new KitchenIngredientMovement { Id = movementLine.GetProperty("line_id").GetGuid(),
                    ShipmentId = upload.Id, IngredientItemId = use.Key, DeltaScaled = -use.Value,
                    CostMinor = ingredientCosts[use.Key], RecipeSnapshotJson = upload.Payload.GetProperty("recipe_snapshot").GetRawText(), OccurredAtUtc = now });
            }
            request.Version++;
            // Confirm & Send finalizes the request even when the kitchen cannot supply
            // every requested unit. The exact shipped quantities remain on the immutable
            // shipment and are what the branch must count.
            request.Status = "FULFILLED";
            configuration.NextDeviceSequence++;
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        finally { store.WriteLock.Release(); }
    }

    public async Task<CustomOrderSnapshot> CreateCustomOrderAsync(Guid customerId, DateTimeOffset dueAtUtc, string description, IReadOnlyDictionary<Guid, long> quantities)
    {
        var selected = quantities.Where(x => x.Value > 0).ToArray();
        if (customerId == Guid.Empty || selected.Length == 0) throw new BusinessRuleException("INVALID_CUSTOM_ORDER", "اختر العميل وصنفاً واحداً على الأقل.");
        if (dueAtUtc <= DateTimeOffset.UtcNow) throw new BusinessRuleException("INVALID_DUE_DATE", "موعد التسليم يجب أن يكون في المستقبل.");
        await store.WriteLock.WaitAsync();
        try
        {
            await using var db = store.Open(); await using var transaction = await db.Database.BeginTransactionAsync();
            var configuration = await db.Configuration.SingleAsync();
            var customer = await db.CafeCustomers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == customerId && x.Active && !x.HiddenLocally)
                ?? throw new BusinessRuleException("CAFE_NOT_FOUND", "العميل غير موجود. نفّذ المزامنة أولاً.");
            var itemIds = selected.Select(x => x.Key).ToArray();
            var prices = await db.CafePrices.AsNoTracking().Include(x => x.Item).Where(x => x.CustomerId == customerId && itemIds.Contains(x.ItemId)).ToDictionaryAsync(x => x.ItemId);
            foreach (var product in await db.Products.AsNoTracking().Where(x => itemIds.Contains(x.Id) && x.Active && x.Kind == "PRODUCT").ToListAsync())
                prices.TryAdd(product.Id, new KitchenCafePrice { CustomerId = customerId, ItemId = product.Id,
                    Item = product, UnitPriceMinor = product.BasePriceMinor, Version = 1 });
            if (prices.Count != selected.Length) throw new BusinessRuleException("CAFE_PRICE_MISSING", "يوجد صنف بلا سعر لهذا العميل.");
            var now = DateTimeOffset.UtcNow; var id = Guid.NewGuid();
            var order = new KitchenCustomOrder { Id = id, CustomerId = customerId, CustomerName = customer.Name, CustomerPhone = customer.Contact,
                Description = (description ?? "").Trim(), DueAtUtc = dueAtUtc, OrderNumber = $"K-CF-{now:yyyyMMdd}-{configuration.NextCustomOrderSequence:0000}", CreatedAtUtc = now, UpdatedAtUtc = now };
            foreach (var input in selected)
            {
                var price = prices[input.Key]; var scale = price.Item.QuantityScale;
                if (input.Value <= 0) throw new BusinessRuleException("INVALID_QUANTITY", "كمية الطلب غير صالحة.");
                var total = checked((2L * input.Value * price.UnitPriceMinor + scale) / (2L * scale));
                order.Lines.Add(new KitchenCustomOrderLine { Id = Guid.NewGuid(), OrderId = id, ItemId = input.Key, ItemName = price.Item.Name,
                    Unit = price.Item.Unit, QuantityScale = scale, QuantityScaled = input.Value, UnitPriceMinor = price.UnitPriceMinor, LineTotalMinor = total });
            }
            order.TotalMinor = order.Lines.Sum(x => x.LineTotalMinor); configuration.NextCustomOrderSequence++;
            var payload = new { custom_order_id = id, customer_id = customerId, site_id = configuration.SiteId, order_number = order.OrderNumber,
                customer_name = order.CustomerName, customer_phone = order.CustomerPhone, description = order.Description, due_at_utc = dueAtUtc,
                total_minor = order.TotalMinor, customer_account_key = order.CustomerPhone, customer_price_version = prices.Values.Max(x => x.Version),
                lines = order.Lines.Select(x => new { line_id = x.Id, item_id = x.ItemId, item_name = x.ItemName, unit = x.Unit,
                    quantity_scale = x.QuantityScale, quantity_scaled = x.QuantityScaled, unit_price_minor = x.UnitPriceMinor, line_total_minor = x.LineTotalMinor }).ToArray(), status = "NEW", version = 1 };
            var contractEvent = ContractEventFactory.Create(id, configuration.NextDeviceSequence, "custom_order.created", now, payload);
            using var document = JsonDocument.Parse(contractEvent.PayloadJson);
            var upload = new SyncUploadEvent(contractEvent.Id, contractEvent.DeviceSequence, contractEvent.EventType, contractEvent.SchemaVersion, contractEvent.OccurredAtUtc.ToString("O"), document.RootElement.Clone(), [], contractEvent.ContentHash);
            var queued = new KitchenOutbox { EventId = id, RequestId = id, Sequence = upload.DeviceSequence, UploadJson = JsonSerializer.Serialize(upload), AppliedLocally = true, NextAttemptAtUtc = now };
            configuration.NextDeviceSequence++;
            db.CustomOrders.Add(order); db.Outbox.Add(queued); await db.SaveChangesAsync(); await transaction.CommitAsync();
            return ToSnapshot(order);
        }
        finally { store.WriteLock.Release(); }
    }

    public async Task<IReadOnlyList<CustomOrderSnapshot>> GetCustomOrdersAsync()
    {
        await using var db = store.Open();
        var orders = await db.CustomOrders.AsNoTracking().Include(x => x.Lines).ToListAsync();
        return orders.OrderByDescending(x => x.CreatedAtUtc).Select(ToSnapshot).ToArray();
    }

    public async Task DeliverCafeOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        await store.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = store.Open(); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var configuration = await db.Configuration.SingleAsync(cancellationToken);
            var order = await db.CustomOrders.Include(x => x.Lines).SingleAsync(x => x.Id == orderId, cancellationToken);
            if (order.Status == "DELIVERED") throw new InvalidOperationException("الطلب سُلّم بالفعل ولا يجوز خصم خاماته مرة أخرى.");
            if (order.Status == "CANCELLED") throw new InvalidOperationException("الطلب ملغي.");
            var productIds = order.Lines.Select(x => x.ItemId).ToArray();
            var recipes = await db.Recipes.Include(x => x.Components)
                .Where(x => productIds.Contains(x.ProductItemId)).ToDictionaryAsync(x => x.ProductItemId, cancellationToken);
            if (recipes.Count != productIds.Length) throw new BusinessRuleException("RECIPE_REQUIRED", "أحد المنتجات بلا وصفة محفوظة.");
            var ingredientUse = new Dictionary<Guid, long>();
            var snapshots = order.Lines.Select(line =>
            {
                var recipe = recipes[line.ItemId];
                var componentRows = recipe.Components.Select(component =>
                {
                    var numerator = checked(line.QuantityScaled * component.QuantityScaled);
                    if (numerator % recipe.OutputScaled != 0) throw new BusinessRuleException("RECIPE_ROUNDING", "كمية الطلب لا تتوافق مع دقة الوصفة.");
                    var used = numerator / recipe.OutputScaled;
                    ingredientUse[component.IngredientItemId] = checked(ingredientUse.GetValueOrDefault(component.IngredientItemId) + used);
                    return new { ingredient_item_id = component.IngredientItemId, quantity_scaled = used.ToString() };
                }).ToArray();
                return new { product_item_id = line.ItemId, output_scaled = line.QuantityScaled.ToString(),
                    recipe_version = recipe.Version, components = componentRows };
            }).ToArray();
            var balances = await db.Ingredients.Where(x => ingredientUse.Keys.Contains(x.ItemId))
                .ToDictionaryAsync(x => x.ItemId, cancellationToken);
            if (balances.Count != ingredientUse.Count || ingredientUse.Any(x => balances[x.Key].QuantityScaled < x.Value))
                throw new BusinessRuleException("INSUFFICIENT_INGREDIENTS", "لا توجد خامات كافية لتجهيز طلب الكافيه.");
            var costs = ingredientUse.ToDictionary(x => x.Key,
                x => CostForUse(balances[x.Key].InventoryCostMinor, balances[x.Key].QuantityScaled, x.Value));
            var eventId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            var payload = new { custom_order_id = order.Id, site_id = configuration.SiteId, status = "DELIVERED",
                version = order.Version + 1, stock_lines = order.Lines.Select(x => new { item_id = x.ItemId,
                    quantity_scaled = x.QuantityScaled.ToString() }).ToArray(), recipe_snapshot = snapshots,
                ingredient_lines = ingredientUse.OrderBy(x => x.Key).Select(x => new { line_id = Guid.NewGuid(),
                    ingredient_item_id = x.Key, quantity_scaled = x.Value.ToString(), cost_minor = costs[x.Key].ToString() }).ToArray(),
                production_cost_minor = costs.Values.Sum().ToString() };
            var contractEvent = ContractEventFactory.Create(eventId, configuration.NextDeviceSequence, "custom_order.status_changed", now, payload);
            using var document = JsonDocument.Parse(contractEvent.PayloadJson);
            var upload = new SyncUploadEvent(contractEvent.Id, contractEvent.DeviceSequence, contractEvent.EventType,
                contractEvent.SchemaVersion, contractEvent.OccurredAtUtc.ToString("O"), document.RootElement.Clone(), [], contractEvent.ContentHash);
            foreach (var use in ingredientUse)
            {
                var balance = balances[use.Key]; balance.QuantityScaled -= use.Value;
                balance.InventoryCostMinor -= costs[use.Key]; balance.Version++;
                var line = upload.Payload.GetProperty("ingredient_lines").EnumerateArray()
                    .Single(x => x.GetProperty("ingredient_item_id").GetGuid() == use.Key);
                db.IngredientMovements.Add(new KitchenIngredientMovement { Id = line.GetProperty("line_id").GetGuid(),
                    ShipmentId = eventId, IngredientItemId = use.Key, Kind = "CAFE_PRODUCTION",
                    DeltaScaled = -use.Value, CostMinor = costs[use.Key], RecipeSnapshotJson = upload.Payload.GetProperty("recipe_snapshot").GetRawText(),
                    OccurredAtUtc = now });
            }
            order.Status = "DELIVERED"; order.Version++; order.UpdatedAtUtc = now;
            db.Outbox.Add(new KitchenOutbox { EventId = eventId, RequestId = orderId, Sequence = configuration.NextDeviceSequence,
                UploadJson = JsonSerializer.Serialize(upload), AppliedLocally = true, NextAttemptAtUtc = now });
            configuration.NextDeviceSequence++;
            await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        }
        finally { store.WriteLock.Release(); }
    }

    public async Task<KitchenProduct> SaveCatalogItemAsync(
        Guid? itemId,
        int? expectedVersion,
        string name,
        string unit,
        int quantityScale,
        string kind = "PRODUCT",
        long basePriceMinor = 0,
        CancellationToken cancellationToken = default)
    {
        name = name.Trim(); unit = unit.Trim(); kind = kind.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(unit))
            throw new InvalidOperationException("اكتب اسم الصنف ووحدته.");
        if (quantityScale is < 1 or > 1000) throw new InvalidOperationException("دقة الوحدة يجب أن تكون بين 1 و1000.");
        if (kind is not ("PRODUCT" or "INGREDIENT")) throw new InvalidOperationException("نوع الصنف غير صالح.");
        if (basePriceMinor < 0 || basePriceMinor > int.MaxValue) throw new InvalidOperationException("سعر البيع غير صالح.");

        await store.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = store.Open();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var configuration = await db.Configuration.SingleAsync(cancellationToken);
            var product = itemId is Guid id ? await db.Products.FindAsync([id], cancellationToken) : null;
            if (itemId is not null && product is null) throw new InvalidOperationException("الصنف غير موجود محلياً.");
            if (product is not null && product.Version != expectedVersion) throw new InvalidOperationException("تم تحديث الصنف؛ افتحه من جديد.");
            var now = DateTimeOffset.UtcNow;
            if (product is null)
            {
                var newId = Guid.NewGuid();
                product = new KitchenProduct { Id = newId, Sku = $"AUTO-{newId:N}", Version = 1, Active = true };
                db.Products.Add(product);
            }
            else product.Version = checked(product.Version + 1);
            product.Name = name; product.Unit = unit; product.QuantityScale = quantityScale; product.Kind = kind;
            product.BasePriceMinor = kind == "PRODUCT" ? basePriceMinor : 0;
            product.Active = true; product.UpdatedAtUtc = now;
            if (kind == "INGREDIENT" && await db.Ingredients.FindAsync([product.Id], cancellationToken) is null)
                db.Ingredients.Add(new KitchenIngredientBalance { ItemId = product.Id, Name = name, Unit = unit, QuantityScale = quantityScale, Version = product.Version });

            var eventId = Guid.NewGuid();
            var payload = new { item_id = product.Id, site_id = configuration.SiteId, sku = product.Sku, name_ar = product.Name,
                unit = product.Unit, quantity_scale = product.QuantityScale, retail_price_minor = product.BasePriceMinor, kind = product.Kind,
                active = product.Active, version = product.Version };
            var contractEvent = ContractEventFactory.Create(eventId, configuration.NextDeviceSequence, "catalog.item.updated", now, payload);
            using var document = JsonDocument.Parse(contractEvent.PayloadJson);
            var upload = new SyncUploadEvent(contractEvent.Id, contractEvent.DeviceSequence, contractEvent.EventType,
                contractEvent.SchemaVersion, contractEvent.OccurredAtUtc.ToString("O"), document.RootElement.Clone(), [], contractEvent.ContentHash);
            db.Outbox.Add(new KitchenOutbox { EventId = eventId, RequestId = product.Id, Sequence = configuration.NextDeviceSequence,
                UploadJson = JsonSerializer.Serialize(upload), AppliedLocally = true, NextAttemptAtUtc = now });
            configuration.NextDeviceSequence++;
            await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
            Trace.WriteLine($"[SYNC] Queued Kitchen catalog item {product.Id}; sequence={upload.DeviceSequence}.");
            return product;
        }
        finally { store.WriteLock.Release(); }
    }

    private static async Task ApplyCatalogItemAsync(KitchenDbContext db, JsonElement payload, Guid kitchenSiteId)
    {
        var id = payload.GetProperty("item_id").GetGuid();
        var version = payload.GetProperty("version").GetInt32();
        var product = await db.Products.FindAsync(id);
        if (product is not null && version <= product.Version) return;
        if (product is null) { product = new KitchenProduct { Id = id }; db.Products.Add(product); }
        product.Sku = payload.GetProperty("sku").GetString() ?? $"AUTO-{id:N}";
        product.Name = payload.GetProperty("name_ar").GetString() ?? throw new InvalidOperationException("اسم الصنف مفقود.");
        product.Unit = payload.GetProperty("unit").GetString() ?? throw new InvalidOperationException("وحدة الصنف مفقودة.");
        product.Kind = payload.TryGetProperty("kind", out var kind) ? kind.GetString() ?? "PRODUCT" : "PRODUCT";
        var priceSite = payload.TryGetProperty("price_site_id", out var publishedSite) ? publishedSite.GetGuid()
            : payload.TryGetProperty("site_id", out var originatingSite) ? originatingSite.GetGuid() : Guid.Empty;
        if (product.Kind == "PRODUCT" && priceSite == kitchenSiteId
            && payload.TryGetProperty("retail_price_minor", out var basePrice)) product.BasePriceMinor = basePrice.GetInt64();
        product.QuantityScale = payload.GetProperty("quantity_scale").GetInt32();
        product.Active = !payload.TryGetProperty("active", out var active) || active.GetBoolean();
        product.Version = version; product.UpdatedAtUtc = DateTimeOffset.UtcNow;
        if (product.Kind == "INGREDIENT")
        {
            var balance = await db.Ingredients.FindAsync(id);
            if (balance is null) db.Ingredients.Add(new KitchenIngredientBalance { ItemId = id, Name = product.Name, Unit = product.Unit, QuantityScale = product.QuantityScale, Version = version });
            else { balance.Name = product.Name; balance.Unit = product.Unit; balance.QuantityScale = product.QuantityScale; balance.Version = Math.Max(balance.Version, version); }
        }
    }

    public async Task<KitchenCafeCustomer> SaveCafeAsync(Guid? customerId, int? expectedVersion,
        string name, string contact, CancellationToken cancellationToken = default)
    {
        name = name.Trim(); contact = contact.Trim();
        if (name.Length is < 1 or > 200 || contact.Length > 200)
            throw new InvalidOperationException("اسم الكافيه أو رقم الاتصال غير صالح.");
        await store.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = store.Open();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var configuration = await db.Configuration.SingleAsync(cancellationToken);
            var cafe = customerId is Guid id ? await db.CafeCustomers.Include(x => x.Prices).SingleOrDefaultAsync(x => x.Id == id, cancellationToken) : null;
            if (customerId is not null && cafe is null) throw new InvalidOperationException("الكافيه غير موجود محلياً.");
            if (cafe is not null && cafe.Version != expectedVersion) throw new InvalidOperationException("تغير الكافيه؛ افتحه من جديد.");
            var isNew = cafe is null;
            if (isNew) { cafe = new KitchenCafeCustomer { Id = Guid.NewGuid(), Version = 1 }; db.CafeCustomers.Add(cafe); }
            else cafe!.Version++;
            cafe!.Name = name; cafe.Contact = contact; cafe.Active = true;
            var now = DateTimeOffset.UtcNow; var eventId = Guid.NewGuid();
            if (isNew)
            {
                var products = await db.Products.Where(x => x.Active && x.Kind == "PRODUCT").ToListAsync(cancellationToken);
                foreach (var product in products) cafe.Prices.Add(new KitchenCafePrice { CustomerId = cafe.Id,
                    ItemId = product.Id, UnitPriceMinor = product.BasePriceMinor, Version = 1 });
            }
            var payload = new { customer_id = cafe.Id, site_id = configuration.SiteId, name = cafe.Name,
                phone = cafe.Contact, notes = cafe.Notes, kind = "Cafe", version = cafe.Version,
                prices = isNew ? cafe.Prices.Select(x => new { item_id = x.ItemId, unit_price_minor = x.UnitPriceMinor }).ToArray() : null };
            var eventType = isNew ? "cafe_customer.created" : "cafe_customer.updated";
            var contractEvent = ContractEventFactory.Create(eventId, configuration.NextDeviceSequence, eventType, now, payload);
            using var document = JsonDocument.Parse(contractEvent.PayloadJson);
            var upload = new SyncUploadEvent(contractEvent.Id, contractEvent.DeviceSequence, contractEvent.EventType,
                contractEvent.SchemaVersion, contractEvent.OccurredAtUtc.ToString("O"), document.RootElement.Clone(), [], contractEvent.ContentHash);
            db.Outbox.Add(new KitchenOutbox { EventId = eventId, RequestId = cafe.Id, Sequence = configuration.NextDeviceSequence,
                UploadJson = JsonSerializer.Serialize(upload), AppliedLocally = true, NextAttemptAtUtc = now });
            configuration.NextDeviceSequence++;
            await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
            return cafe;
        }
        finally { store.WriteLock.Release(); }
    }

    public async Task SaveRecipeAsync(Guid productId, int expectedVersion, long outputScaled,
        IReadOnlyDictionary<Guid, long> components, CancellationToken cancellationToken = default)
    {
        if (outputScaled <= 0 || components.Count == 0 || components.Values.Any(x => x <= 0))
            throw new InvalidOperationException("اكتب كمية الناتج ومكوناً واحداً على الأقل.");
        await store.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = store.Open(); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var configuration = await db.Configuration.SingleAsync(cancellationToken);
            var product = await db.Products.FindAsync([productId], cancellationToken);
            if (product is null || !product.Active || product.Kind != "PRODUCT") throw new InvalidOperationException("اختر منتجاً صالحاً.");
            var ingredientIds = components.Keys.ToArray();
            var ingredientCount = await db.Products.CountAsync(x => ingredientIds.Contains(x.Id) && x.Active && x.Kind == "INGREDIENT", cancellationToken);
            if (ingredientCount != components.Count) throw new InvalidOperationException("الوصفة تحتوي على خامة غير متاحة.");
            var recipe = await db.Recipes.Include(x => x.Components).SingleOrDefaultAsync(x => x.ProductItemId == productId, cancellationToken);
            if ((recipe?.Version ?? 0) != expectedVersion) throw new InvalidOperationException("تغيرت الوصفة؛ افتحها من جديد.");
            if (recipe is null) { recipe = new KitchenRecipeRecord { ProductItemId = productId }; db.Recipes.Add(recipe); }
            else { db.RecipeComponents.RemoveRange(recipe.Components); recipe.Components.Clear(); }
            recipe.OutputScaled = outputScaled; recipe.Version = expectedVersion + 1;
            foreach (var component in components) recipe.Components.Add(new KitchenRecipeComponentRecord {
                Id = Guid.NewGuid(), ProductItemId = productId, IngredientItemId = component.Key, QuantityScaled = component.Value });
            var eventId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            var payload = new { product_item_id = productId, output_scaled = outputScaled.ToString(), version = recipe.Version,
                components = components.Select(x => new { ingredient_item_id = x.Key, quantity_scaled = x.Value.ToString() }).ToArray() };
            var contractEvent = ContractEventFactory.Create(eventId, configuration.NextDeviceSequence, "recipe.updated", now, payload);
            using var document = JsonDocument.Parse(contractEvent.PayloadJson);
            var upload = new SyncUploadEvent(contractEvent.Id, contractEvent.DeviceSequence, contractEvent.EventType,
                contractEvent.SchemaVersion, contractEvent.OccurredAtUtc.ToString("O"), document.RootElement.Clone(), [], contractEvent.ContentHash);
            db.Outbox.Add(new KitchenOutbox { EventId = eventId, RequestId = productId, Sequence = configuration.NextDeviceSequence,
                UploadJson = JsonSerializer.Serialize(upload), AppliedLocally = true, NextAttemptAtUtc = now });
            configuration.NextDeviceSequence++;
            await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        }
        finally { store.WriteLock.Release(); }
    }

    public async Task<IReadOnlyList<KitchenReceiptRecord>> GetReceiptsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = store.Open();
        // SQLite stores DateTimeOffset values as text and EF Core cannot translate
        // ordering for that CLR type. Materialize first so opening the Kitchen UI
        // cannot crash as soon as a receipt exists in the local database.
        var receipts = await db.Receipts.AsNoTracking().ToListAsync(cancellationToken);
        return receipts.OrderByDescending(x => x.CountedAtUtc).ToArray();
    }

    public async Task HideCafeCustomerAsync(Guid customerId)
    {
        if (customerId == Guid.Empty) throw new BusinessRuleException("CAFE_REQUIRED", "اختر الكافيه أولاً.");
        await store.WriteLock.WaitAsync();
        try
        {
            await using var db = store.Open();
            var customer = await db.CafeCustomers.SingleOrDefaultAsync(x => x.Id == customerId)
                ?? throw new BusinessRuleException("CAFE_NOT_FOUND", "الكافيه غير موجود.");
            customer.HiddenLocally = true;
            customer.Active = false;
            await db.SaveChangesAsync();
        }
        finally { store.WriteLock.Release(); }
    }

    public async Task PrintCustomOrderAsync(Guid orderId, string printerName)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("الطباعة المباشرة متاحة على Windows فقط.");
        await using var db = store.Open(); var configuration = await db.Configuration.SingleAsync();
        var order = await db.CustomOrders.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == orderId);
        await new WindowsRasterBranchPrinter().PrintCustomOrderAsync(printerName, configuration.SiteName, ToSnapshot(order));
        configuration.PrinterName = printerName.Trim(); await db.SaveChangesAsync();
    }

    private static CustomOrderSnapshot ToSnapshot(KitchenCustomOrder order) => new(order.Id, order.CustomerId, order.OrderNumber, order.CustomerName,
        order.CustomerPhone, order.Description, order.DueAtUtc, order.TotalMinor, order.PaidMinor, Math.Max(0, order.TotalMinor - order.PaidMinor),
        Math.Max(0, order.TotalMinor - order.PaidMinor), Enum.TryParse<CustomOrderStatus>(order.Status, true, out var status) ? status : CustomOrderStatus.New,
        order.Version, order.CreatedAtUtc, order.UpdatedAtUtc, false, order.Lines.Select(x => new CustomOrderLineSnapshot(x.ItemId, x.ItemName, x.Unit,
            x.QuantityScale, x.QuantityScaled, x.UnitPriceMinor, x.LineTotalMinor)).ToArray());

    public Task ReceiveIngredientsAsync(IReadOnlyDictionary<Guid, long> quantities, string reason) => PostIngredientMovementAsync("ingredient.received", quantities, reason);
    public Task ReceiveIngredientPurchaseAsync(Guid ingredientId, long quantityScaled, long totalCostMinor, string reason)
    {
        if (ingredientId == Guid.Empty || quantityScaled <= 0 || totalCostMinor <= 0)
            throw new InvalidOperationException("اختر خامة، كمية وسعر شراء أكبر من الصفر.");
        return PostIngredientMovementAsync("ingredient.received",
            new Dictionary<Guid, long> { [ingredientId] = quantityScaled }, reason,
            new Dictionary<Guid, long> { [ingredientId] = totalCostMinor });
    }
    public Task RecordWasteAsync(IReadOnlyDictionary<Guid, long> quantities, string reason) => PostIngredientMovementAsync("ingredient.waste", quantities, reason);
    private async Task PostIngredientMovementAsync(string eventType, IReadOnlyDictionary<Guid, long> quantities, string reason,
        IReadOnlyDictionary<Guid, long>? purchaseCosts = null)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500) throw new InvalidOperationException("اكتب سبباً واضحاً للعملية.");
        var selected = quantities.Where(x => x.Value > 0).ToArray(); if (selected.Length == 0) throw new InvalidOperationException("أدخل كمية لخامة واحدة على الأقل.");
        await store.WriteLock.WaitAsync();
        try
        {
            await using var db = store.Open(); await using var transaction = await db.Database.BeginTransactionAsync();
            var configuration = await db.Configuration.SingleAsync();
            var balances = await db.Ingredients.Where(x => selected.Select(y => y.Key).Contains(x.ItemId)).ToDictionaryAsync(x => x.ItemId);
            if (balances.Count != selected.Length || eventType == "ingredient.waste" && selected.Any(x => balances[x.Key].QuantityScaled < x.Value)) throw new InvalidOperationException("رصيد الخامات لا يسمح بهذه العملية.");
            var eventId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            var payload = new { operation_id = eventId, reason = reason.Trim(), lines = selected.Select(x => new {
                line_id = Guid.NewGuid(), item_id = x.Key, quantity_scaled = x.Value.ToString(),
                total_cost_minor = (purchaseCosts?.GetValueOrDefault(x.Key) ?? 0).ToString() }).ToArray() };
            var contractEvent = ContractEventFactory.Create(eventId, configuration.NextDeviceSequence, eventType, now, payload); using var document = JsonDocument.Parse(contractEvent.PayloadJson);
            var upload = new SyncUploadEvent(contractEvent.Id, contractEvent.DeviceSequence, contractEvent.EventType, contractEvent.SchemaVersion, contractEvent.OccurredAtUtc.ToString("O"), document.RootElement.Clone(), [], contractEvent.ContentHash);
            var queued = new KitchenOutbox { EventId = eventId, RequestId = eventId, Sequence = upload.DeviceSequence, UploadJson = JsonSerializer.Serialize(upload), AppliedLocally = true, NextAttemptAtUtc = now };
            var multiplier = eventType == "ingredient.received" ? 1L : -1L;
            foreach (var input in selected)
            {
                var balance = balances[input.Key];
                var cost = eventType == "ingredient.received" ? purchaseCosts?.GetValueOrDefault(input.Key) ?? 0
                    : CostForUse(balance.InventoryCostMinor, balance.QuantityScaled, input.Value);
                balance.QuantityScaled = checked(balance.QuantityScaled + multiplier * input.Value);
                balance.InventoryCostMinor = checked(balance.InventoryCostMinor + multiplier * cost);
                balance.Version++;
                var line = upload.Payload.GetProperty("lines").EnumerateArray().Single(x => x.GetProperty("item_id").GetGuid() == input.Key);
                db.IngredientMovements.Add(new KitchenIngredientMovement { Id = line.GetProperty("line_id").GetGuid(), ShipmentId = upload.Id,
                    IngredientItemId = input.Key, Kind = eventType == "ingredient.received" ? "RECEIPT" : "WASTE", Reason = reason.Trim(),
                    DeltaScaled = multiplier * input.Value, CostMinor = cost, OccurredAtUtc = now });
            }
            configuration.NextDeviceSequence++; db.Outbox.Add(queued); await db.SaveChangesAsync(); await transaction.CommitAsync();
        }
        finally { store.WriteLock.Release(); }
    }

    public async Task CountIngredientsAsync(IReadOnlyDictionary<Guid, long> counts)
    {
        await store.WriteLock.WaitAsync();
        try
        {
            await using var db = store.Open(); await using var transaction = await db.Database.BeginTransactionAsync(); var configuration = await db.Configuration.SingleAsync();
            var balances = await db.Ingredients.ToListAsync(); if (balances.Count == 0 || balances.Any(x => !counts.ContainsKey(x.ItemId) || counts[x.ItemId] < 0)) throw new InvalidOperationException("يجب عد كل الخامات بكميات صحيحة.");
            var now = DateTimeOffset.UtcNow; var eventId = Guid.NewGuid(); var cairo = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Egypt Standard Time" : "Africa/Cairo"); var date = TimeZoneInfo.ConvertTime(now, cairo).Date.ToString("yyyy-MM-dd");
            var todayWaste = await db.IngredientMovements.Where(x => x.Kind == "WASTE" && x.OccurredAtUtc >= now.Date).GroupBy(x => x.IngredientItemId).Select(x => new { x.Key, Total = -x.Sum(y => y.DeltaScaled) }).ToDictionaryAsync(x => x.Key, x => x.Total);
            var payload = new { count_id = eventId, business_date = date, lines = balances.Select(x => new { line_id = Guid.NewGuid(), item_id = x.ItemId, actual_scaled = counts[x.ItemId].ToString(), recorded_waste_scaled = todayWaste.GetValueOrDefault(x.ItemId).ToString() }).ToArray() };
            var contractEvent = ContractEventFactory.Create(eventId, configuration.NextDeviceSequence, "ingredient.counted", now, payload); using var document = JsonDocument.Parse(contractEvent.PayloadJson);
            var upload = new SyncUploadEvent(contractEvent.Id, contractEvent.DeviceSequence, contractEvent.EventType, contractEvent.SchemaVersion, contractEvent.OccurredAtUtc.ToString("O"), document.RootElement.Clone(), [], contractEvent.ContentHash);
            var queued = new KitchenOutbox { EventId = eventId, RequestId = eventId, Sequence = upload.DeviceSequence, UploadJson = JsonSerializer.Serialize(upload), AppliedLocally = true, NextAttemptAtUtc = now };
            foreach (var balance in balances)
            {
                var actual = counts[balance.ItemId]; var delta = actual - balance.QuantityScaled;
                var cost = delta < 0 ? CostForUse(balance.InventoryCostMinor, balance.QuantityScaled, -delta) : 0;
                balance.InventoryCostMinor -= cost; balance.QuantityScaled = actual; balance.Version++;
                var line = upload.Payload.GetProperty("lines").EnumerateArray().Single(x => x.GetProperty("item_id").GetGuid() == balance.ItemId);
                db.IngredientMovements.Add(new KitchenIngredientMovement { Id = line.GetProperty("line_id").GetGuid(), ShipmentId = upload.Id,
                    IngredientItemId = balance.ItemId, Kind = "COUNT", Reason = "جرد نهاية اليوم", DeltaScaled = delta, CostMinor = cost, OccurredAtUtc = now });
            }
            configuration.NextDeviceSequence++; db.Outbox.Add(queued); await db.SaveChangesAsync(); await transaction.CommitAsync();
        }
        finally { store.WriteLock.Release(); }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default, bool forceRetry = false)
    {
        await store.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = store.Open();
            while (true)
            {
                var pending = await db.Outbox.Where(x => !x.Acknowledged).OrderBy(x => x.Sequence)
                    .FirstOrDefaultAsync(cancellationToken);
                if (pending is null) break;
                if (pending.PermanentlyFailed)
                    throw new InvalidOperationException($"رفض الخادم عملية محفوظة ({pending.LastErrorCode ?? "SYNC_REJECTED"}). لم تُحذف وتحتاج مراجعة.");
                if (!forceRetry && pending.NextAttemptAtUtc > DateTimeOffset.UtcNow) break;
                var configuration = await db.Configuration.SingleAsync(cancellationToken);
                try
                {
                    await SendPendingAsync(db, pending, configuration, cancellationToken);
                }
                catch (CentralApiException exception)
                {
                    MarkFailure(pending, exception.Code, exception.SafeMessage, exception.Retryable);
                    await db.SaveChangesAsync(cancellationToken);
                    if (!exception.Retryable)
                        throw new InvalidOperationException($"رفض الخادم عملية محفوظة ({exception.Code}). لم تُحذف وتحتاج مراجعة.", exception);
                    break;
                }
                catch (HttpRequestException exception)
                {
                    MarkFailure(pending, "OFFLINE", exception.Message, true);
                    await db.SaveChangesAsync(cancellationToken);
                    break;
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    MarkFailure(pending, "TIMEOUT", "انتهت مهلة الاتصال بالخادم.", true);
                    await db.SaveChangesAsync(cancellationToken);
                    break;
                }
            }
        }
        finally { store.WriteLock.Release(); }
    }

    private async Task SendPendingAsync(KitchenDbContext db, KitchenOutbox pending, KitchenConfiguration configuration, CancellationToken cancellationToken)
    {
        var upload = JsonSerializer.Deserialize<SyncUploadEvent>(pending.UploadJson)!;
        var response = await new CentralApiClient(http).PushAsync(Connect(configuration), configuration.StreamEpoch, [upload], cancellationToken);
        if (response.Results.Length != 1 || response.Results[0].Id != upload.Id)
            throw new CentralApiException("INVALID_SYNC_RESPONSE", "وصل رد مزامنة غير مكتمل.", true, 502);
        var result = response.Results[0];
        if (result.Status is not ("accepted" or "duplicate"))
            throw new CentralApiException(result.Code ?? result.ErrorCode ?? "SYNC_REJECTED", "رفض الخادم العملية؛ بقيت محفوظة.", result.Retryable ?? result.Status == "retryable", 409);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!pending.AppliedLocally && upload.EventType == "shipment.dispatched") {
        var request = await db.Requests.Include(x => x.Lines).SingleAsync(x => x.Id == pending.RequestId);
        foreach (var row in upload.Payload.GetProperty("lines").EnumerateArray())
            request.Lines.Single(x => x.Id == row.GetProperty("request_line_id").GetGuid()).SentScaled += row.GetProperty("sent_scaled").GetInt64();
        foreach (var row in upload.Payload.GetProperty("ingredient_lines").EnumerateArray())
        {
            var itemId = row.GetProperty("ingredient_item_id").GetGuid(); var quantity = ParseInteger(row.GetProperty("quantity_scaled")); var balance = await db.Ingredients.FindAsync(itemId);
            if (balance is null || balance.QuantityScaled < quantity) throw new InvalidOperationException("رصيد الخامات المحلي تغير بعد إرسال الشحنة؛ أوقف التشغيل وراجع الإدارة.");
            balance.QuantityScaled -= quantity; balance.Version++;
            var cost = row.TryGetProperty("cost_minor", out var costValue) ? ParseInteger(costValue, true) : 0;
            balance.InventoryCostMinor -= cost;
            db.IngredientMovements.Add(new KitchenIngredientMovement { Id = row.TryGetProperty("line_id", out var lineId) ? lineId.GetGuid() : Guid.NewGuid(),
                ShipmentId = upload.Id, IngredientItemId = itemId, DeltaScaled = -quantity, CostMinor = cost,
                RecipeSnapshotJson = upload.Payload.GetProperty("recipe_snapshot").GetRawText(), OccurredAtUtc = DateTimeOffset.Parse(upload.OccurredAt) });
        }
        request.Version++;
        request.Status = request.Lines.All(x => x.SentScaled >= x.RequestedScaled) ? "FULFILLED" : "PARTIAL";
        } else if (!pending.AppliedLocally && upload.EventType is "ingredient.received" or "ingredient.waste") {
            var multiplier = upload.EventType == "ingredient.received" ? 1L : -1L; var reason = upload.Payload.GetProperty("reason").GetString()!;
            foreach (var row in upload.Payload.GetProperty("lines").EnumerateArray()) { var itemId = row.GetProperty("item_id").GetGuid(); var quantity = ParseInteger(row.GetProperty("quantity_scaled")); var balance = await db.Ingredients.FindAsync(itemId) ?? throw new InvalidOperationException("الخامة غير موجودة محلياً."); var cost = multiplier > 0 ? row.TryGetProperty("total_cost_minor", out var costValue) ? ParseInteger(costValue, true) : 0 : CostForUse(balance.InventoryCostMinor, balance.QuantityScaled, quantity); balance.QuantityScaled = checked(balance.QuantityScaled + multiplier * quantity); balance.InventoryCostMinor = checked(balance.InventoryCostMinor + multiplier * cost); balance.Version++; db.IngredientMovements.Add(new KitchenIngredientMovement { Id = row.GetProperty("line_id").GetGuid(), ShipmentId = upload.Id, IngredientItemId = itemId, Kind = upload.EventType == "ingredient.received" ? "RECEIPT" : "WASTE", Reason = reason, DeltaScaled = multiplier * quantity, CostMinor = cost, OccurredAtUtc = DateTimeOffset.Parse(upload.OccurredAt) }); }
        } else if (!pending.AppliedLocally && upload.EventType == "ingredient.counted") {
            foreach (var row in upload.Payload.GetProperty("lines").EnumerateArray()) { var itemId = row.GetProperty("item_id").GetGuid(); var actual = ParseInteger(row.GetProperty("actual_scaled"), true); var balance = await db.Ingredients.FindAsync(itemId) ?? throw new InvalidOperationException("الخامة غير موجودة محلياً."); var delta = actual - balance.QuantityScaled; var cost = delta < 0 ? CostForUse(balance.InventoryCostMinor, balance.QuantityScaled, -delta) : 0; balance.InventoryCostMinor -= cost; balance.QuantityScaled = actual; balance.Version++; db.IngredientMovements.Add(new KitchenIngredientMovement { Id = row.GetProperty("line_id").GetGuid(), ShipmentId = upload.Id, IngredientItemId = itemId, Kind = "COUNT", Reason = "جرد نهاية اليوم", DeltaScaled = delta, CostMinor = cost, OccurredAtUtc = DateTimeOffset.Parse(upload.OccurredAt) }); }
        }
        if (response.NextExpectedSequence <= pending.Sequence) throw new InvalidOperationException("استجابة تسلسل المزامنة غير صالحة.");
        configuration.NextDeviceSequence = Math.Max(configuration.NextDeviceSequence, response.NextExpectedSequence);
        pending.AppliedLocally = true;
        pending.Acknowledged = true;
        pending.LastErrorCode = null;
        pending.LastErrorMessage = null;
        pending.PermanentlyFailed = false;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static void MarkFailure(KitchenOutbox pending, string code, string message, bool retryable)
    {
        pending.Attempts = checked(pending.Attempts + 1);
        pending.LastErrorCode = code;
        pending.LastErrorMessage = message.Length <= 500 ? message : message[..500];
        pending.PermanentlyFailed = !retryable;
        var baseDelay = Math.Min(60, Math.Pow(2, Math.Min(pending.Attempts, 6)));
        var seconds = Math.Min(60, baseDelay * (0.8 + Random.Shared.NextDouble() * 0.4));
        pending.NextAttemptAtUtc = DateTimeOffset.UtcNow.AddSeconds(seconds);
    }

    private static DeviceConnection Connect(KitchenConfiguration c) => new(new Uri(c.ApiBaseUrl), c.DeviceId, DeviceCredentialProtector.Unprotect(c.ProtectedCredential));

    private async Task RecoverMissingRequestAcknowledgementsAsync(CancellationToken cancellationToken)
    {
        await store.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = store.Open();
            var requests = await db.Requests.Where(value => value.Status == "REQUESTED").ToListAsync(cancellationToken);
            if (requests.Count == 0) return;
            var queued = await db.Outbox.ToListAsync(cancellationToken);
            foreach (var request in requests)
            {
                var alreadyQueued = queued.Any(value => value.RequestId == request.Id
                    && value.UploadJson.Contains("kitchen_request.received", StringComparison.Ordinal));
                if (!alreadyQueued) QueueRequestAcknowledgement(db, request, null);
                request.Status = "RECEIVED";
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        finally { store.WriteLock.Release(); }
    }

    private static void ApplyRequest(KitchenDbContext db, SyncPulledEvent incoming)
    {
        var p = incoming.Payload; var requestId = p.GetProperty("request_id").GetGuid();
        if (db.Requests.Any(x => x.Id == requestId)) return;
        var request = new KitchenRequestRecord { Id = requestId, BranchSiteId = incoming.OriginSiteId,
            BranchName = p.TryGetProperty("branch_name", out var name) ? name.GetString()! : incoming.OriginSiteId.ToString()[..8],
            Status = "RECEIVED", Version = p.GetProperty("version").GetInt32(), SubmittedAtUtc = DateTimeOffset.Parse(incoming.OccurredAt) };
        foreach (var row in p.GetProperty("lines").EnumerateArray()) request.Lines.Add(new KitchenRequestLineRecord {
            Id = row.GetProperty("line_id").GetGuid(), ItemId = row.GetProperty("item_id").GetGuid(),
            Name = row.GetProperty("name_snapshot").GetString()!, Unit = row.GetProperty("unit_snapshot").GetString()!,
            QuantityScale = checked((int)ReadQuantity(row.GetProperty("quantity_scale"))), RequestedScaled = ReadQuantity(row.GetProperty("requested_scaled")) });
        db.Requests.Add(request);

        QueueRequestAcknowledgement(db, request, incoming.Id);
    }

    private static void QueueRequestAcknowledgement(KitchenDbContext db, KitchenRequestRecord request, Guid? sourceEventId)
    {
        var configuration = db.Configuration.Single();
        var acknowledgedAt = DateTimeOffset.UtcNow;
        var acknowledgementId = Guid.NewGuid();
        var contractEvent = ContractEventFactory.Create(
            acknowledgementId,
            configuration.NextDeviceSequence,
            "kitchen_request.received",
            acknowledgedAt,
            new
            {
                request_id = request.Id,
                destination_site_id = request.BranchSiteId,
                status = "RECEIVED",
                version = checked(request.Version + 1),
                received_at = acknowledgedAt.ToString("O")
            },
            sourceEventId is Guid dependency ? [dependency] : []);
        using var document = JsonDocument.Parse(contractEvent.PayloadJson);
        var upload = new SyncUploadEvent(
            contractEvent.Id,
            contractEvent.DeviceSequence,
            contractEvent.EventType,
            contractEvent.SchemaVersion,
            contractEvent.OccurredAtUtc.ToString("O"),
            document.RootElement.Clone(),
            sourceEventId is Guid dependencyId ? [dependencyId.ToString()] : [],
            contractEvent.ContentHash);
        db.Outbox.Add(new KitchenOutbox
        {
            EventId = acknowledgementId,
            RequestId = request.Id,
            Sequence = configuration.NextDeviceSequence,
            UploadJson = JsonSerializer.Serialize(upload),
            AppliedLocally = true,
            NextAttemptAtUtc = acknowledgedAt
        });
        configuration.NextDeviceSequence += 1;
    }
    private static void ApplyReceipt(KitchenDbContext db, SyncPulledEvent incoming)
    {
        var payload = incoming.Payload;
        db.Receipts.Add(new KitchenReceiptRecord { EventId = incoming.Id, ShipmentId = payload.GetProperty("shipment_id").GetGuid(),
            ReceiptId = payload.GetProperty("receipt_id").GetGuid(), BranchSiteId = incoming.OriginSiteId,
            Status = incoming.EventType == "incoming_receipt.accepted" ? "ACCEPTED" : "DISPUTED",
            PayloadJson = payload.GetRawText(), CountedAtUtc = DateTimeOffset.Parse(incoming.OccurredAt) });
    }
    private static void ApplyReturn(KitchenDbContext db, SyncPulledEvent incoming)
    {
        var payload = incoming.Payload;
        db.Returns.Add(new KitchenReturnRecord { EventId = incoming.Id, ReturnId = payload.GetProperty("return_id").GetGuid(),
            BranchSiteId = incoming.OriginSiteId, Reference = payload.GetProperty("reference").GetString() ?? "",
            PayloadJson = payload.GetRawText(), DispatchedAtUtc = DateTimeOffset.Parse(incoming.OccurredAt) });
    }
    private static long ReadQuantity(JsonElement value)
    {
        var result = value.ValueKind == JsonValueKind.String ? long.Parse(value.GetString()!, System.Globalization.CultureInfo.InvariantCulture) : value.GetInt64();
        if (result <= 0) throw new IOException("Invalid shipment quantity");
        return result;
    }
}
