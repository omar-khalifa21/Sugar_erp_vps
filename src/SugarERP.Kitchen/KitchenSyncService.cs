using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SugarERP.Application;
using SugarERP.Domain;
using SugarERP.Infrastructure.Local;
using SugarERP.Sync.Client;

namespace SugarERP.Kitchen;

public sealed class KitchenSyncService(HttpClient http, KitchenStore store)
{
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

    public async Task<int> PullAsync()
    {
        await FlushAsync();
        await BootstrapAsync();
        var total = 0;
        while (true)
        {
            var result = await PullPageAsync();
            total += result.Applied;
            if (!result.HasMore) return total;
        }
    }
    private async Task BootstrapAsync()
    {
        await using var read = store.Open();
        var configuration = await read.Configuration.AsNoTracking().SingleAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, configuration.ApiBaseUrl + "/sync/bootstrap");
        request.Headers.Add("x-device-id", configuration.DeviceId.ToString()); request.Headers.Add("x-device-secret", DeviceCredentialProtector.Unprotect(configuration.ProtectedCredential));
        using var response = await http.SendAsync(request); response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var catalog = document.RootElement.GetProperty("catalog").EnumerateArray().ToDictionary(x => x.GetProperty("id").GetGuid());
        await store.WriteLock.WaitAsync();
        try
        {
            await using var db = store.Open(); await using var transaction = await db.Database.BeginTransactionAsync();
            var currentConfiguration = await db.Configuration.SingleAsync();
            currentConfiguration.SiteName = document.RootElement.GetProperty("site").GetProperty("name").GetString() ?? "المطبخ";
            foreach (var item in catalog.Values.Where(x => x.GetProperty("kind").GetString() == "PRODUCT"))
            {
                var id = item.GetProperty("id").GetGuid(); var product = await db.Products.FindAsync(id);
                if (product is null) { product = new KitchenProduct { Id = id }; db.Products.Add(product); }
                product.Name = item.GetProperty("nameAr").GetString()!; product.Unit = item.GetProperty("unit").GetString()!;
                product.QuantityScale = item.GetProperty("quantityScale").GetInt32(); product.Active = true;
            }
            foreach (var row in document.RootElement.GetProperty("customers").EnumerateArray())
            {
                var id = row.GetProperty("id").GetGuid(); var customer = await db.CafeCustomers.Include(x => x.Prices).SingleOrDefaultAsync(x => x.Id == id);
                if (customer is null) { customer = new KitchenCafeCustomer { Id = id }; db.CafeCustomers.Add(customer); }
                customer.Name = row.GetProperty("name").GetString()!; customer.Contact = row.TryGetProperty("contact", out var contact) ? contact.GetString() ?? "" : "";
                customer.Notes = row.TryGetProperty("notes", out var notes) ? notes.GetString() ?? "" : ""; customer.Active = true;
                db.CafePrices.RemoveRange(customer.Prices); customer.Prices.Clear();
                foreach (var price in row.GetProperty("prices").EnumerateArray()) customer.Prices.Add(new KitchenCafePrice {
                    CustomerId = id, ItemId = price.GetProperty("itemId").GetGuid(), UnitPriceMinor = price.GetProperty("priceMinor").GetInt64(), Version = price.GetProperty("version").GetInt32() });
            }
            foreach (var row in document.RootElement.GetProperty("recipes").EnumerateArray())
            {
                var productId = row.GetProperty("product_item_id").GetGuid(); var recipe = await db.Recipes.Include(x => x.Components).SingleOrDefaultAsync(x => x.ProductItemId == productId);
                if (recipe is null) { recipe = new KitchenRecipeRecord { ProductItemId = productId }; db.Recipes.Add(recipe); }
                var version = row.GetProperty("version").GetInt32(); if (version < recipe.Version) continue;
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
            }
            await db.SaveChangesAsync(); await transaction.CommitAsync();
        }
        finally { store.WriteLock.Release(); }
    }
    private static long ParseInteger(JsonElement value, bool allowZero = false) { var parsed = value.ValueKind == JsonValueKind.String ? long.Parse(value.GetString()!, System.Globalization.CultureInfo.InvariantCulture) : value.GetInt64(); if (parsed < 0 || (!allowZero && parsed == 0)) throw new IOException("Invalid central quantity"); return parsed; }
    private async Task<(int Applied, bool HasMore)> PullPageAsync()
    {
        await using var read = store.Open();
        var configuration = await read.Configuration.AsNoTracking().SingleAsync();
        var connection = Connect(configuration);
        var page = await new CentralApiClient(http).PullAsync(connection, configuration.Cursor, 100);
        await store.WriteLock.WaitAsync();
        try
        {
            await using var db = store.Open();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var applied = 0;
            foreach (var incoming in page.Events.OrderBy(x => long.Parse(x.ServerPosition)))
            {
                var seen = await db.Inbox.FindAsync(incoming.Id);
                if (seen is not null) { if (!seen.ContentHash.Equals(incoming.ContentHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("تعارض حدث مزامنة."); continue; }
                if (incoming.EventType == "kitchen_request.submitted") ApplyRequest(db, incoming);
                if (incoming.EventType is "incoming_receipt.accepted" or "incoming_receipt.disputed") ApplyReceipt(db, incoming);
                if (incoming.EventType == "kitchen_return.dispatched") ApplyReturn(db, incoming);
                db.Inbox.Add(new KitchenInbox { EventId = incoming.Id, ContentHash = incoming.ContentHash, AppliedAtUtc = DateTimeOffset.UtcNow });
                applied++;
            }
            var current = await db.Configuration.SingleAsync();
            current.Cursor = page.Cursor;
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            await new CentralApiClient(http).AcknowledgeAsync(connection, page.Cursor);
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
            if (request.Status is "FULFILLED" or "REJECTED") throw new InvalidOperationException("الطلب مغلق.");
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
            var shipmentId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var payload = new {
                shipment_id = shipmentId, request_id = request.Id, destination_site_id = request.BranchSiteId,
                reference = $"K-{now:yyyyMMdd}-{configuration.NextDeviceSequence:000000}", version = 1, dispatched_at = now.ToString("O"),
                lines = selected.Select(x => new { line_id = Guid.NewGuid(), request_line_id = x.Id, item_id = x.ItemId,
                    sent_scaled = quantities[x.Id], quantity_scale = x.QuantityScale, name_snapshot = x.Name, unit_snapshot = x.Unit }).ToArray(),
                recipe_snapshot = recipeSnapshot,
                ingredient_lines = ingredientUse.OrderBy(x => x.Key).Select(x => new { ingredient_item_id = x.Key, quantity_scaled = x.Value.ToString() }).ToArray()
            };
            var contractEvent = ContractEventFactory.Create(shipmentId, configuration.NextDeviceSequence, "shipment.dispatched", now, payload);
            using var document = JsonDocument.Parse(contractEvent.PayloadJson);
            var upload = new SyncUploadEvent(contractEvent.Id, contractEvent.DeviceSequence, contractEvent.EventType, contractEvent.SchemaVersion,
                contractEvent.OccurredAtUtc.ToString("O"), document.RootElement.Clone(), [], contractEvent.ContentHash);
            var queued = new KitchenOutbox { EventId = upload.Id, RequestId = requestId, Sequence = upload.DeviceSequence, UploadJson = JsonSerializer.Serialize(upload), AppliedLocally = true };
            db.Outbox.Add(queued);
            foreach (var line in selected) line.SentScaled = checked(line.SentScaled + quantities[line.Id]);
            foreach (var use in ingredientUse)
            {
                var balance = balances[use.Key]; balance.QuantityScaled -= use.Value; balance.Version++;
                db.IngredientMovements.Add(new KitchenIngredientMovement { Id = Guid.NewGuid(), ShipmentId = upload.Id, IngredientItemId = use.Key,
                    DeltaScaled = -use.Value, RecipeSnapshotJson = upload.Payload.GetProperty("recipe_snapshot").GetRawText(), OccurredAtUtc = now });
            }
            request.Version++;
            request.Status = request.Lines.All(x => x.SentScaled >= x.RequestedScaled) ? "FULFILLED" : "PARTIAL";
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
            var queued = new KitchenOutbox { EventId = id, RequestId = id, Sequence = upload.DeviceSequence, UploadJson = JsonSerializer.Serialize(upload), AppliedLocally = true };
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
    public Task RecordWasteAsync(IReadOnlyDictionary<Guid, long> quantities, string reason) => PostIngredientMovementAsync("ingredient.waste", quantities, reason);
    private async Task PostIngredientMovementAsync(string eventType, IReadOnlyDictionary<Guid, long> quantities, string reason)
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
            var payload = new { operation_id = eventId, reason = reason.Trim(), lines = selected.Select(x => new { line_id = Guid.NewGuid(), item_id = x.Key, quantity_scaled = x.Value.ToString() }).ToArray() };
            var contractEvent = ContractEventFactory.Create(eventId, configuration.NextDeviceSequence, eventType, now, payload); using var document = JsonDocument.Parse(contractEvent.PayloadJson);
            var upload = new SyncUploadEvent(contractEvent.Id, contractEvent.DeviceSequence, contractEvent.EventType, contractEvent.SchemaVersion, contractEvent.OccurredAtUtc.ToString("O"), document.RootElement.Clone(), [], contractEvent.ContentHash);
            var queued = new KitchenOutbox { EventId = eventId, RequestId = eventId, Sequence = upload.DeviceSequence, UploadJson = JsonSerializer.Serialize(upload), AppliedLocally = true };
            var multiplier = eventType == "ingredient.received" ? 1L : -1L;
            foreach (var input in selected)
            {
                var balance = balances[input.Key]; balance.QuantityScaled = checked(balance.QuantityScaled + multiplier * input.Value); balance.Version++;
                var line = upload.Payload.GetProperty("lines").EnumerateArray().Single(x => x.GetProperty("item_id").GetGuid() == input.Key);
                db.IngredientMovements.Add(new KitchenIngredientMovement { Id = line.GetProperty("line_id").GetGuid(), ShipmentId = upload.Id,
                    IngredientItemId = input.Key, Kind = eventType == "ingredient.received" ? "RECEIPT" : "WASTE", Reason = reason.Trim(),
                    DeltaScaled = multiplier * input.Value, OccurredAtUtc = now });
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
            var queued = new KitchenOutbox { EventId = eventId, RequestId = eventId, Sequence = upload.DeviceSequence, UploadJson = JsonSerializer.Serialize(upload), AppliedLocally = true };
            foreach (var balance in balances)
            {
                var actual = counts[balance.ItemId]; var delta = actual - balance.QuantityScaled; balance.QuantityScaled = actual; balance.Version++;
                var line = upload.Payload.GetProperty("lines").EnumerateArray().Single(x => x.GetProperty("item_id").GetGuid() == balance.ItemId);
                db.IngredientMovements.Add(new KitchenIngredientMovement { Id = line.GetProperty("line_id").GetGuid(), ShipmentId = upload.Id,
                    IngredientItemId = balance.ItemId, Kind = "COUNT", Reason = "جرد نهاية اليوم", DeltaScaled = delta, OccurredAtUtc = now });
            }
            configuration.NextDeviceSequence++; db.Outbox.Add(queued); await db.SaveChangesAsync(); await transaction.CommitAsync();
        }
        finally { store.WriteLock.Release(); }
    }

    public async Task FlushAsync()
    {
        await store.WriteLock.WaitAsync();
        try
        {
            await using var db = store.Open();
            while (true)
            {
                var pending = await db.Outbox.Where(x => !x.Acknowledged).OrderBy(x => x.Sequence).FirstOrDefaultAsync();
                if (pending is null) break;
                await SendPendingAsync(db, pending, await db.Configuration.SingleAsync());
            }
        }
        finally { store.WriteLock.Release(); }
    }

    private async Task SendPendingAsync(KitchenDbContext db, KitchenOutbox pending, KitchenConfiguration configuration)
    {
        var upload = JsonSerializer.Deserialize<SyncUploadEvent>(pending.UploadJson)!;
        var response = await new CentralApiClient(http).PushAsync(Connect(configuration), configuration.StreamEpoch, [upload]);
        if (response.Results.Length != 1 || response.Results[0].Id != upload.Id || response.Results[0].Status is not ("accepted" or "duplicate"))
            throw new InvalidOperationException("رفض الخادم الشحنة؛ بقيت محفوظة لإعادة المحاولة.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (!pending.AppliedLocally && upload.EventType == "shipment.dispatched") {
        var request = await db.Requests.Include(x => x.Lines).SingleAsync(x => x.Id == pending.RequestId);
        foreach (var row in upload.Payload.GetProperty("lines").EnumerateArray())
            request.Lines.Single(x => x.Id == row.GetProperty("request_line_id").GetGuid()).SentScaled += row.GetProperty("sent_scaled").GetInt64();
        foreach (var row in upload.Payload.GetProperty("ingredient_lines").EnumerateArray())
        {
            var itemId = row.GetProperty("ingredient_item_id").GetGuid(); var quantity = ParseInteger(row.GetProperty("quantity_scaled")); var balance = await db.Ingredients.FindAsync(itemId);
            if (balance is null || balance.QuantityScaled < quantity) throw new InvalidOperationException("رصيد الخامات المحلي تغير بعد إرسال الشحنة؛ أوقف التشغيل وراجع الإدارة.");
            balance.QuantityScaled -= quantity; balance.Version++;
            db.IngredientMovements.Add(new KitchenIngredientMovement { Id = Guid.NewGuid(), ShipmentId = upload.Id, IngredientItemId = itemId, DeltaScaled = -quantity,
                RecipeSnapshotJson = upload.Payload.GetProperty("recipe_snapshot").GetRawText(), OccurredAtUtc = DateTimeOffset.Parse(upload.OccurredAt) });
        }
        request.Version++;
        request.Status = request.Lines.All(x => x.SentScaled >= x.RequestedScaled) ? "FULFILLED" : "PARTIAL";
        } else if (!pending.AppliedLocally && upload.EventType is "ingredient.received" or "ingredient.waste") {
            var multiplier = upload.EventType == "ingredient.received" ? 1L : -1L; var reason = upload.Payload.GetProperty("reason").GetString()!;
            foreach (var row in upload.Payload.GetProperty("lines").EnumerateArray()) { var itemId = row.GetProperty("item_id").GetGuid(); var quantity = ParseInteger(row.GetProperty("quantity_scaled")); var balance = await db.Ingredients.FindAsync(itemId) ?? throw new InvalidOperationException("الخامة غير موجودة محلياً."); balance.QuantityScaled = checked(balance.QuantityScaled + multiplier * quantity); balance.Version++; db.IngredientMovements.Add(new KitchenIngredientMovement { Id = row.GetProperty("line_id").GetGuid(), ShipmentId = upload.Id, IngredientItemId = itemId, Kind = upload.EventType == "ingredient.received" ? "RECEIPT" : "WASTE", Reason = reason, DeltaScaled = multiplier * quantity, OccurredAtUtc = DateTimeOffset.Parse(upload.OccurredAt) }); }
        } else if (!pending.AppliedLocally && upload.EventType == "ingredient.counted") {
            foreach (var row in upload.Payload.GetProperty("lines").EnumerateArray()) { var itemId = row.GetProperty("item_id").GetGuid(); var actual = ParseInteger(row.GetProperty("actual_scaled"), true); var balance = await db.Ingredients.FindAsync(itemId) ?? throw new InvalidOperationException("الخامة غير موجودة محلياً."); var delta = actual - balance.QuantityScaled; balance.QuantityScaled = actual; balance.Version++; db.IngredientMovements.Add(new KitchenIngredientMovement { Id = row.GetProperty("line_id").GetGuid(), ShipmentId = upload.Id, IngredientItemId = itemId, Kind = "COUNT", Reason = "جرد نهاية اليوم", DeltaScaled = delta, OccurredAtUtc = DateTimeOffset.Parse(upload.OccurredAt) }); }
        }
        if (response.NextExpectedSequence <= pending.Sequence) throw new InvalidOperationException("استجابة تسلسل المزامنة غير صالحة.");
        configuration.NextDeviceSequence = Math.Max(configuration.NextDeviceSequence, response.NextExpectedSequence);
        pending.AppliedLocally = true;
        pending.Acknowledged = true;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static DeviceConnection Connect(KitchenConfiguration c) => new(new Uri(c.ApiBaseUrl), c.DeviceId, DeviceCredentialProtector.Unprotect(c.ProtectedCredential));
    private static void ApplyRequest(KitchenDbContext db, SyncPulledEvent incoming)
    {
        var p = incoming.Payload; var requestId = p.GetProperty("request_id").GetGuid();
        if (db.Requests.Any(x => x.Id == requestId)) return;
        var request = new KitchenRequestRecord { Id = requestId, BranchSiteId = incoming.OriginSiteId,
            BranchName = p.TryGetProperty("branch_name", out var name) ? name.GetString()! : incoming.OriginSiteId.ToString()[..8],
            Version = p.GetProperty("version").GetInt32(), SubmittedAtUtc = DateTimeOffset.Parse(incoming.OccurredAt) };
        foreach (var row in p.GetProperty("lines").EnumerateArray()) request.Lines.Add(new KitchenRequestLineRecord {
            Id = row.GetProperty("line_id").GetGuid(), ItemId = row.GetProperty("item_id").GetGuid(),
            Name = row.GetProperty("name_snapshot").GetString()!, Unit = row.GetProperty("unit_snapshot").GetString()!,
            QuantityScale = checked((int)ReadQuantity(row.GetProperty("quantity_scale"))), RequestedScaled = ReadQuantity(row.GetProperty("requested_scaled")) });
        db.Requests.Add(request);
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
