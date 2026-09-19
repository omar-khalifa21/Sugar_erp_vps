using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SugarERP.Application;
using SugarERP.Domain;

namespace SugarERP.Infrastructure.Local;

public sealed record Branch2InventoryCommand(Guid CommandId, Guid ReferenceId, Guid UserId, Branch2TransactionKind Kind,
    IReadOnlyDictionary<Guid, long> Quantities, string Reason, string Authorization = "");

public sealed class Branch2InventoryService(LocalDatabase database)
{
    public async Task<BranchInventoryTransaction> ExecuteAsync(Branch2InventoryCommand command, CancellationToken ct = default)
    {
        if (command.CommandId == Guid.Empty || command.ReferenceId == Guid.Empty || command.UserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.Reason) || string.IsNullOrWhiteSpace(command.Authorization) || command.Quantities.Count > 100 || command.Quantities.Any(x => x.Key == Guid.Empty || x.Value <= 0))
            throw new BusinessRuleException("INVALID_INVENTORY_COMMAND", "راجع مرجع العملية والكميات والسبب.");
        if (command.Kind == Branch2TransactionKind.ApprovedAdjustment)
            throw new BusinessRuleException("ADMIN_DECISION_REQUIRED", "تعديل الرصيد يتطلب قرار إدارة موثق.");
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            command.ReferenceId, command.UserId, command.Kind, Reason = command.Reason.Trim(),
            Quantities = command.Quantities.OrderBy(x => x.Key).ToArray()
        }))));
        await database.WriteLock.WaitAsync(ct);
        try
        {
            await using var db = database.CreateContext();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var configuration = await db.DeviceConfigurations.SingleAsync(ct);
            if (configuration.Profile != DeviceProfile.BranchType2) throw new BusinessRuleException("WRONG_PROFILE", "هذه العملية خاصة بالفرع نوع ٢.");
            var replay = await db.InventoryTransactions.Include(x => x.Lines)
                .SingleOrDefaultAsync(x => x.Id == command.CommandId || (x.Kind == command.Kind && x.ReferenceId == command.ReferenceId), ct);
            if (replay is not null)
            {
                if (replay.Fingerprint != fingerprint) throw new BusinessRuleException("IDEMPOTENCY_KEY_REUSE", "مرجع العملية مستخدم لمحتوى مختلف.");
                return replay;
            }
            var quantities = command.Quantities.ToDictionary();
            if (command.Kind == Branch2TransactionKind.DisplayReturnToStock)
            {
                var display = await db.LocationBalances.Where(x => x.Location == BranchInventoryLocation.Display && x.QuantityScaled > 0).ToListAsync(ct);
                if (quantities.Count != 0 && (quantities.Count != display.Count || display.Any(x => quantities.GetValueOrDefault(x.ItemId) != x.QuantityScaled)))
                    throw new BusinessRuleException("DISPLAY_COUNT_MISMATCH", "الكمية المعدودة تختلف عن الرصيد؛ يلزم قرار إدارة قبل إغلاق اليوم.");
                quantities = display.ToDictionary(x => x.ItemId, x => x.QuantityScaled);
            }
            else if (quantities.Count == 0) throw new BusinessRuleException("EMPTY_INVENTORY_COMMAND", "اختر الأصناف أولاً.");
            if (command.Kind == Branch2TransactionKind.IncomingReceipt)
            {
                var receipt = await db.IncomingReceipts.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == command.ReferenceId, ct);
                if (receipt is null || receipt.Status is not (IncomingReceiptStatus.Accepted or IncomingReceiptStatus.Resolved) ||
                    receipt.Lines.Count != quantities.Count || receipt.Lines.Any(x => x.ConfirmedScaled != quantities.GetValueOrDefault(x.ItemId)))
                    throw new BusinessRuleException("RECEIPT_NOT_ACCEPTED", "الوارد يجب أن يطابق استلاماً مقبولاً أو قرار إدارة موثقاً.");
            }
            var record = new BranchInventoryTransaction { Id = command.CommandId, ReferenceId = command.ReferenceId, SiteId = configuration.SiteId,
                UserId = command.UserId, Kind = command.Kind, Reason = command.Reason.Trim(), Fingerprint = fingerprint, OccurredAtUtc = DateTimeOffset.UtcNow };
            foreach (var (itemId, quantity) in quantities)
            {
                if (!await db.CatalogItems.AnyAsync(x => x.Id == itemId && x.Active, ct)) throw new BusinessRuleException("ITEM_NOT_FOUND", "الصنف غير متاح.");
                if (command.Kind == Branch2TransactionKind.StockToDisplay) { await Leg(itemId, BranchInventoryLocation.Stock, -quantity); await Leg(itemId, BranchInventoryLocation.Display, quantity); }
                else if (command.Kind == Branch2TransactionKind.DisplayReturnToStock) { await Leg(itemId, BranchInventoryLocation.Display, -quantity); await Leg(itemId, BranchInventoryLocation.Stock, quantity); }
                else await Leg(itemId, command.Kind == Branch2TransactionKind.RetailSale ? BranchInventoryLocation.Display : BranchInventoryLocation.Stock,
                    command.Kind == Branch2TransactionKind.IncomingReceipt ? quantity : -quantity);
            }
            db.InventoryTransactions.Add(record);
            var sequence = await db.SequenceStates.SingleAsync(ct);
            var ev = ContractEventFactory.Create(command.CommandId, sequence.NextDeviceSequence++, "branch2.inventory.posted", record.OccurredAtUtc, new {
                transaction_id = record.Id, reference_id = record.ReferenceId, user_id = record.UserId, authorization = command.Authorization, kind = record.Kind.ToString(), reason = record.Reason,
                lines = record.Lines.Select(x => new { item_id = x.ItemId, location = x.Location == BranchInventoryLocation.Stock ? "FREEZER" : "DISPLAY", delta_scaled = x.DeltaScaled.ToString() }).ToArray()
            });
            db.OutboxMessages.Add(new OutboxMessage { EventId = ev.Id, AggregateId = record.Id, DeviceSequence = ev.DeviceSequence, EventType = ev.EventType,
                OccurredAtUtc = ev.OccurredAtUtc, PayloadJson = ev.PayloadJson, ContentHash = ev.ContentHash, DependenciesJson = ev.DependenciesJson,
                NextAttemptAtUtc = ev.OccurredAtUtc });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return record;

            async Task Leg(Guid itemId, BranchInventoryLocation location, long delta)
            {
                var balance = await db.LocationBalances.FindAsync([itemId, location], ct);
                if (balance is null) { balance = new BranchLocationBalance { ItemId = itemId, Location = location }; db.LocationBalances.Add(balance); }
                var next = checked(balance.QuantityScaled + delta);
                if (next < 0) throw new BusinessRuleException("INSUFFICIENT_STOCK", location == BranchInventoryLocation.Stock ? "المخزون غير كافٍ." : "رصيد العرض غير كافٍ؛ انقل من المخزون أولاً.");
                balance.QuantityScaled = next; balance.Version++;
                record.Lines.Add(new BranchInventoryTransactionLine { Id = Guid.NewGuid(), ItemId = itemId, Location = location, DeltaScaled = delta });
            }
        }
        finally { database.WriteLock.Release(); }
    }
}
