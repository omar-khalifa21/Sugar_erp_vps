using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.Json;
using SugarERP.Application;
using SugarERP.Domain;
using SugarERP.Infrastructure.Local;
using Xunit;

namespace SugarERP.Branch1.Tests;

public sealed class BranchOperationsServiceTests
{
    private static readonly Guid ChocolateCakeId = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid ChocolateGateauxId = Guid.Parse("10000000-0000-4000-8000-000000000002");

    [Fact]
    public async Task SaveEnrollmentAsync_ReusesStaleSequenceStateAndPreservesUnsentEvents()
    {
        await using var store = await TestStore.CreateAsync(seedFixtures: false);
        var acknowledgedEvent = ContractEventFactory.Create(Guid.NewGuid(), 1, "catalog.item.updated", DateTimeOffset.UtcNow, new { item_id = Guid.NewGuid() });
        var pendingEvent = ContractEventFactory.Create(Guid.NewGuid(), 5, "kitchen_request.submitted", DateTimeOffset.UtcNow, new { request_id = Guid.NewGuid() });
        await using (var setup = store.Database.CreateContext())
        {
            setup.SequenceStates.Add(new SequenceState { NextDeviceSequence = 6 });
            setup.OutboxMessages.AddRange(
                new OutboxMessage { EventId = acknowledgedEvent.Id, AggregateId = Guid.NewGuid(), DeviceSequence = 1, EventType = acknowledgedEvent.EventType, OccurredAtUtc = acknowledgedEvent.OccurredAtUtc, PayloadJson = acknowledgedEvent.PayloadJson, DependenciesJson = acknowledgedEvent.DependenciesJson, ContentHash = acknowledgedEvent.ContentHash, State = OutboxState.Acknowledged, NextAttemptAtUtc = DateTimeOffset.UtcNow },
                new OutboxMessage { EventId = pendingEvent.Id, AggregateId = Guid.NewGuid(), DeviceSequence = 5, EventType = pendingEvent.EventType, OccurredAtUtc = pendingEvent.OccurredAtUtc, PayloadJson = pendingEvent.PayloadJson, DependenciesJson = pendingEvent.DependenciesJson, ContentHash = pendingEvent.ContentHash, State = OutboxState.Failed, LastErrorCode = "UNAUTHENTICATED", Attempts = 3, NextAttemptAtUtc = DateTimeOffset.UtcNow });
            await setup.SaveChangesAsync();
        }

        var siteId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        await store.Operations.SaveEnrollmentAsync(
            new EnrollmentCommand(new Uri("https://ascendyz.xyz/api/v1"), "one-time-token", "BRANCH-PC", "thumbprint", "1.2.6", true, DeviceProfile.BranchType1),
            new EnrollmentResult(siteId, deviceId, DeviceProfile.BranchType1, "new-device-secret", 1));

        await using var verify = store.Database.CreateContext();
        var configuration = await verify.DeviceConfigurations.SingleAsync();
        var outbox = await verify.OutboxMessages.SingleAsync();
        Assert.Equal(deviceId, configuration.DeviceId);
        Assert.Equal("https://ascendyz.xyz/api/v1", configuration.ApiBaseUrl);
        Assert.Equal(1, outbox.DeviceSequence);
        Assert.Equal(OutboxState.Pending, outbox.State);
        Assert.Null(outbox.LastErrorCode);
        Assert.Equal(2, (await verify.SequenceStates.SingleAsync()).NextDeviceSequence);
        Assert.NotEqual(pendingEvent.ContentHash, outbox.ContentHash);
    }

    [Fact]
    public async Task ClearEnrollmentAsync_RemovesOnlyTransportIdentityAndPreservesBusinessData()
    {
        await using var store = await TestStore.CreateAsync(seedFixtures: false);
        var siteId = Guid.NewGuid();
        await store.Operations.SaveEnrollmentAsync(
            new EnrollmentCommand(new Uri("https://ascendyz.xyz/api/v1"), "token", "BRANCH-PC", "thumbprint", "1.2.7", true, DeviceProfile.BranchType1),
            new EnrollmentResult(siteId, Guid.NewGuid(), DeviceProfile.BranchType1, "secret", 1));
        var pending = ContractEventFactory.Create(Guid.NewGuid(), 1, "kitchen_request.submitted", DateTimeOffset.UtcNow, new { request_id = Guid.NewGuid() });
        await using (var setup = store.Database.CreateContext())
        {
            setup.OutboxMessages.Add(new OutboxMessage
            {
                EventId = pending.Id, AggregateId = Guid.NewGuid(), DeviceSequence = 1, EventType = pending.EventType,
                OccurredAtUtc = pending.OccurredAtUtc, PayloadJson = pending.PayloadJson, DependenciesJson = pending.DependenciesJson,
                ContentHash = pending.ContentHash, State = OutboxState.Pending, NextAttemptAtUtc = DateTimeOffset.UtcNow
            });
            setup.SyncCursors.Add(new SyncCursor { FeedScope = "device", Cursor = "old", UpdatedAtUtc = DateTimeOffset.UtcNow });
            setup.InboxMessages.Add(new InboxMessage { EventId = Guid.NewGuid(), ContentHash = new string('a', 64), AppliedAtUtc = DateTimeOffset.UtcNow });
            await setup.SaveChangesAsync();
        }

        await store.Operations.ClearEnrollmentAsync();

        await using var verify = store.Database.CreateContext();
        Assert.Empty(await verify.DeviceConfigurations.ToListAsync());
        Assert.Empty(await verify.SyncCursors.ToListAsync());
        Assert.Empty(await verify.InboxMessages.ToListAsync());
        Assert.Equal(pending.Id, (await verify.OutboxMessages.SingleAsync()).EventId);
        Assert.Equal(1, (await verify.SequenceStates.SingleAsync()).NextDeviceSequence);
    }

    [Fact]
    public async Task InitializeAsync_AppliesInitialMigrationAndRequiredConnectionPragmas()
    {
        await using var store = await TestStore.CreateAsync(seedFixtures: false);
        await store.Database.InitializeAsync();

        Assert.True(File.Exists(store.Database.DatabasePath));
        await using var db = store.Database.CreateContext();
        var migrations = await db.Database.GetAppliedMigrationsAsync();
        Assert.Equal(db.Database.GetMigrations(), migrations);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        await db.Database.OpenConnectionAsync();
        Assert.Equal("wal", await ReadPragmaAsync(db, "journal_mode"));
        Assert.Equal("1", await ReadPragmaAsync(db, "foreign_keys"));

        var tables = await db.Database.SqlQueryRaw<string>(
                "SELECT name AS Value FROM sqlite_master WHERE type = 'table'")
            .ToListAsync();
        Assert.Contains("device_configuration", tables);
        Assert.Contains("stock_balances", tables);
        Assert.Contains("shifts", tables);
        Assert.Contains("sales", tables);
        Assert.Contains("sync_outbox", tables);
        Assert.Contains("kitchen_requests", tables);
        Assert.Contains("shipments", tables);
        Assert.Contains("quantity_conflicts", tables);
        Assert.Contains("stock_counts", tables);
        Assert.Contains("report_artifacts", tables);
    }

    [Fact]
    public async Task InitializeAsync_UsesSqliteOnlineBackupBeforeSchemaUpgrade()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sugar-erp-branch1-upgrade-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var database = new LocalDatabase(Path.Combine(directory, "branch-type-1.db"));
        try
        {
            await using (var initial = database.CreateContext())
            {
                var migrator = initial.GetService<IMigrator>();
                await migrator.MigrateAsync("20260909221057_InitialBranchType1");
            }

            await database.InitializeAsync();

            var backups = Directory.GetFiles(Path.Combine(directory, "backups"), "*-pre-upgrade-*.db");
            var backupPath = Assert.Single(backups);
            Assert.True(new FileInfo(backupPath).Length > 0);
            var backup = new LocalDatabase(backupPath);
            await using var backupContext = backup.CreateContext();
            Assert.Equal(
                ["20260909221057_InitialBranchType1"],
                await backupContext.Database.GetAppliedMigrationsAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InitializeAsync_DoesNotSeedOperationalData()
    {
        await using var store = await TestStore.CreateAsync(seedFixtures: false);

        var snapshot = await store.Operations.GetSnapshotAsync();
        Assert.Null(snapshot.Configuration);
        Assert.Empty(snapshot.Items);

        await using var db = store.Database.CreateContext();
        Assert.Empty(await db.CatalogItems.ToListAsync());
        Assert.Empty(await db.StockBalances.ToListAsync());
        Assert.Empty(await db.StockMovements.ToListAsync());
        Assert.Empty(await db.SequenceStates.ToListAsync());
    }

    [Fact]
    public async Task OpenShiftAsync_PersistsOpeningSnapshotAndRejectsSecondOpenShift()
    {
        await using var store = await TestStore.CreateAsync();

        var opened = await store.Operations.OpenShiftAsync(ShiftKind.Morning, 12_500);

        var restarted = new BranchOperationsService(new LocalDatabase(store.Database.DatabasePath));
        var snapshot = await restarted.GetSnapshotAsync();
        Assert.NotNull(snapshot.OpenShift);
        Assert.Equal(opened.Id, snapshot.OpenShift.Id);
        Assert.Equal(ShiftKind.Morning, snapshot.OpenShift.Kind);
        Assert.Equal(12_500, snapshot.OpenShift.OpeningCashMinor);

        await using var db = store.Database.CreateContext();
        var cakeOpening = await db.ShiftItemSnapshots.SingleAsync(
            value => value.ShiftId == opened.Id && value.ItemId == ChocolateCakeId);
        Assert.Equal(18, cakeOpening.OpeningQuantityScaled);
        Assert.Equal(18, cakeOpening.ExpectedCloseScaled);

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => restarted.OpenShiftAsync(ShiftKind.Evening, 0));
        Assert.Equal("SHIFT_ALREADY_OPEN", exception.Code);
    }

    [Fact]
    public async Task CompleteSaleAsync_CommitsMoneyStockSnapshotsLedgerAndOutboxTogether()
    {
        await using var store = await TestStore.CreateAsync();
        var shift = await store.Operations.OpenShiftAsync(ShiftKind.Morning, 10_000);
        var command = CreateTwoItemSale(Guid.NewGuid());

        var receipt = await store.Operations.CompleteSaleAsync(command);

        Assert.False(receipt.WasAlreadyCommitted);
        Assert.Equal(79_000, receipt.SubtotalMinor);
        Assert.Equal(9_000, receipt.DiscountMinor);
        Assert.Equal(1_000, receipt.TipMinor);
        Assert.Equal(71_000, receipt.TotalMinor);
        Assert.Equal(PaymentMethod.Cash, receipt.PaymentMethod);

        await using var db = store.Database.CreateContext();
        var sale = await db.Sales
            .Include(value => value.Lines)
            .Include(value => value.Payments)
            .SingleAsync(value => value.Id == receipt.Id);
        Assert.Equal(2, sale.Lines.Count);
        Assert.Equal(sale.DiscountMinor, sale.Lines.Sum(value => value.AllocatedDiscountMinor));
        Assert.Equal(sale.SubtotalMinor - sale.DiscountMinor, sale.Lines.Sum(value => value.TotalMinor));
        Assert.Collection(
            sale.Payments,
            payment =>
            {
                Assert.Equal(PaymentMethod.Cash, payment.Method);
                Assert.Equal(71_000, payment.AmountMinor);
            });

        Assert.Equal(17, (await db.StockBalances.SingleAsync(value => value.ItemId == ChocolateCakeId)).QuantityScaled);
        Assert.Equal(42, (await db.StockBalances.SingleAsync(value => value.ItemId == ChocolateGateauxId)).QuantityScaled);

        var cakeSnapshot = await db.ShiftItemSnapshots.SingleAsync(
            value => value.ShiftId == shift.Id && value.ItemId == ChocolateCakeId);
        var gateauxSnapshot = await db.ShiftItemSnapshots.SingleAsync(
            value => value.ShiftId == shift.Id && value.ItemId == ChocolateGateauxId);
        Assert.Equal(1, cakeSnapshot.SoldScaled);
        Assert.Equal(17, cakeSnapshot.ExpectedCloseScaled);
        Assert.Equal(4, gateauxSnapshot.SoldScaled);
        Assert.Equal(42, gateauxSnapshot.ExpectedCloseScaled);

        var movements = await db.StockMovements
            .Where(value => value.DocumentId == sale.Id)
            .OrderBy(value => value.ItemId)
            .ToListAsync();
        Assert.Equal(2, movements.Count);
        Assert.Contains(movements, value => value.ItemId == ChocolateCakeId && value.DeltaScaled == -1);
        Assert.Contains(movements, value => value.ItemId == ChocolateGateauxId && value.DeltaScaled == -4);

        var cash = await db.CashMovements.SingleAsync(value => value.SourceId == sale.Id);
        Assert.Equal("SALE_CASH", cash.Kind);
        Assert.Equal(71_000, cash.AmountMinor);

        var outbox = await db.OutboxMessages.OrderBy(value => value.DeviceSequence).ToListAsync();
        Assert.Equal(2, outbox.Count);
        Assert.Equal("shift.opened", outbox[0].EventType);
        Assert.Equal("sale.completed", outbox[1].EventType);
        Assert.Equal(new[] { 1, 2 }, outbox.Select(message => message.DeviceSequence).ToArray());
        Assert.Equal(sale.Id, outbox[1].AggregateId);
        Assert.Equal("[]", outbox[1].DependenciesJson);
        Assert.All(outbox, message => Assert.Matches("^[0-9a-f]{64}$", message.ContentHash));

        using var payload = JsonDocument.Parse(outbox[1].PayloadJson);
        Assert.Equal(sale.Id, payload.RootElement.GetProperty("sale_id").GetGuid());
        Assert.Equal(71_000, payload.RootElement.GetProperty("total_minor").GetInt64());
        Assert.Equal(2, payload.RootElement.GetProperty("lines").GetArrayLength());
    }

    [Fact]
    public async Task CompleteSaleAsync_ReplayingSameCommandIdReturnsOriginalWithoutDuplicateEffects()
    {
        await using var store = await TestStore.CreateAsync();
        await store.Operations.OpenShiftAsync(ShiftKind.Morning, 0);
        var command = CreateTwoItemSale(Guid.NewGuid());

        var first = await store.Operations.CompleteSaleAsync(command);
        var replay = await store.Operations.CompleteSaleAsync(command);

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(first.ReceiptNumber, replay.ReceiptNumber);
        Assert.True(replay.WasAlreadyCommitted);

        await using var db = store.Database.CreateContext();
        Assert.Equal(1, await db.Sales.CountAsync());
        Assert.Equal(2, await db.SaleLines.CountAsync());
        Assert.Equal(2, await db.StockMovements.CountAsync(value => value.Kind == StockMovementKind.RetailSale));
        Assert.Equal(1, await db.CashMovements.CountAsync());
        Assert.Equal(2, await db.OutboxMessages.CountAsync());
        Assert.Equal(17, (await db.StockBalances.SingleAsync(value => value.ItemId == ChocolateCakeId)).QuantityScaled);
        Assert.Equal(42, (await db.StockBalances.SingleAsync(value => value.ItemId == ChocolateGateauxId)).QuantityScaled);
    }

    [Fact]
    public async Task CompleteSaleAsync_ReusingCommandIdWithDifferentInputIsRejectedWithoutDuplicateEffects()
    {
        await using var store = await TestStore.CreateAsync();
        await store.Operations.OpenShiftAsync(ShiftKind.Morning, 0);
        var commandId = Guid.NewGuid();
        var first = CreateTwoItemSale(commandId);
        await store.Operations.CompleteSaleAsync(first);
        var conflictingReplay = first with
        {
            Lines = [new SaleCartLine(ChocolateCakeId, 2)],
            DiscountMinor = 0,
            TipMinor = 0
        };

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => store.Operations.CompleteSaleAsync(conflictingReplay));

        Assert.Equal("IDEMPOTENCY_KEY_REUSE", exception.Code);
        await using var db = store.Database.CreateContext();
        Assert.Equal(1, await db.Sales.CountAsync());
        Assert.Equal(2, await db.SaleLines.CountAsync());
        Assert.Equal(2, await db.StockMovements.CountAsync(value => value.Kind == StockMovementKind.RetailSale));
        Assert.Equal(1, await db.CashMovements.CountAsync());
        Assert.Equal(2, await db.OutboxMessages.CountAsync());
        Assert.Equal(17, (await db.StockBalances.SingleAsync(value => value.ItemId == ChocolateCakeId)).QuantityScaled);
        Assert.Equal(42, (await db.StockBalances.SingleAsync(value => value.ItemId == ChocolateGateauxId)).QuantityScaled);
    }

    [Fact]
    public async Task CompleteSaleAsync_WhenStockIsInsufficient_RollsBackAllSaleEffects()
    {
        await using var store = await TestStore.CreateAsync();
        await store.Operations.OpenShiftAsync(ShiftKind.Morning, 0);
        var command = new CompleteSaleCommand(
            Guid.NewGuid(),
            [new SaleCartLine(ChocolateCakeId, 19)],
            PaymentMethod.Cash,
            FulfillmentKind.Takeaway,
            0,
            0);

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => store.Operations.CompleteSaleAsync(command));

        Assert.Equal("INSUFFICIENT_STOCK", exception.Code);
        await using var db = store.Database.CreateContext();
        Assert.Empty(await db.Sales.ToListAsync());
        Assert.Empty(await db.SaleLines.ToListAsync());
        Assert.Empty(await db.SalePayments.ToListAsync());
        Assert.Empty(await db.CashMovements.ToListAsync());
        Assert.Empty(await db.StockMovements.Where(value => value.Kind == StockMovementKind.RetailSale).ToListAsync());
        Assert.Equal(18, (await db.StockBalances.SingleAsync(value => value.ItemId == ChocolateCakeId)).QuantityScaled);
        Assert.Single(await db.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task SaleAndOpenShiftRemainAvailableAfterDatabaseRestart()
    {
        await using var store = await TestStore.CreateAsync();
        var shift = await store.Operations.OpenShiftAsync(ShiftKind.Evening, 25_000);
        var receipt = await store.Operations.CompleteSaleAsync(CreateTwoItemSale(Guid.NewGuid()));

        var restartedDatabase = new LocalDatabase(store.Database.DatabasePath);
        await restartedDatabase.InitializeAsync();
        var restartedOperations = new BranchOperationsService(restartedDatabase);
        var snapshot = await restartedOperations.GetSnapshotAsync();

        Assert.NotNull(snapshot.OpenShift);
        Assert.Equal(shift.Id, snapshot.OpenShift.Id);
        Assert.Equal(1, snapshot.OpenShift.ReceiptCount);
        Assert.Equal(receipt.TotalMinor, snapshot.OpenShift.SalesMinor);
        Assert.Equal(17, snapshot.Items.Single(item => item.Id == ChocolateCakeId).QuantityScaled);
        Assert.Equal(42, snapshot.Items.Single(item => item.Id == ChocolateGateauxId).QuantityScaled);
        Assert.Equal(2, snapshot.PendingOutboxCount);

        await using var db = restartedDatabase.CreateContext();
        Assert.Equal(receipt.Id, (await db.Sales.SingleAsync()).Id);
        Assert.Equal(3, (await db.SequenceStates.SingleAsync()).NextDeviceSequence);
        Assert.Equal(2, (await db.SequenceStates.SingleAsync()).NextReceiptSequence);
    }

    private static CompleteSaleCommand CreateTwoItemSale(Guid commandId) => new(
        commandId,
        [new SaleCartLine(ChocolateCakeId, 1), new SaleCartLine(ChocolateGateauxId, 4)],
        PaymentMethod.Cash,
        FulfillmentKind.Takeaway,
        DiscountMinor: 9_000,
        TipMinor: 1_000);

    private static async Task<string> ReadPragmaAsync(BranchDbContext db, string pragma)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"PRAGMA {pragma};";
        return Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture)
            ?? string.Empty;
    }

    private sealed class TestStore : IAsyncDisposable
    {
        private readonly string _directory;

        private TestStore(string directory)
        {
            _directory = directory;
            Database = new LocalDatabase(Path.Combine(directory, "branch-type-1.db"));
            Operations = new BranchOperationsService(Database);
        }

        public LocalDatabase Database { get; }

        public BranchOperationsService Operations { get; }

        public static async Task<TestStore> CreateAsync(bool seedFixtures = true)
        {
            var directory = Path.Combine(Path.GetTempPath(), "sugar-erp-branch1-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var store = new TestStore(directory);
            await store.Operations.InitializeAsync();
            if (seedFixtures) await BranchTestFixtureSeeder.SeedAsync(store.Database);
            return store;
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
            return ValueTask.CompletedTask;
        }
    }
}
