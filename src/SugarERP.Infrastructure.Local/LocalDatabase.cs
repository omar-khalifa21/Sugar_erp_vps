using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text;
using SugarERP.Domain;

namespace SugarERP.Infrastructure.Local;

public sealed class LocalDatabase(string databasePath)
{
    public string DatabasePath { get; } = Path.GetFullPath(databasePath);
    public SemaphoreSlim WriteLock { get; } = new(1, 1);

    public BranchDbContext CreateContext()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            DefaultTimeout = 5,
            Pooling = true
        }.ToString();
        var options = new DbContextOptionsBuilder<BranchDbContext>()
            .UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly(typeof(BranchDbContext).Assembly.FullName))
            .EnableDetailedErrors()
            .Options;
        return new BranchDbContext(options);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath) ?? ".");
        await using var db = CreateContext();
        var databaseExisted = File.Exists(DatabasePath) && new FileInfo(DatabasePath).Length > 0;
        var pendingMigrations = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
        if (databaseExisted && pendingMigrations.Length > 0)
            await CreateUpdateBackupAsync("pre-upgrade", cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
        await BackfillLegacyCafeProfilesAsync(db, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("PRAGMA synchronous=FULL;", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;", cancellationToken);
    }

    private static async Task BackfillLegacyCafeProfilesAsync(BranchDbContext db, CancellationToken cancellationToken)
    {
        var legacyOrders = await db.CustomOrders.Include(value => value.Payments)
            .Where(value => value.CafeCustomerId == null)
            .ToListAsync(cancellationToken);
        if (legacyOrders.Count == 0) return;

        var catalog = await db.CatalogItems.AsNoTracking().Where(value => value.Active).ToListAsync(cancellationToken);
        var customers = await db.CafeCustomers.Include(value => value.Prices).ToListAsync(cancellationToken);
        foreach (var group in legacyOrders.GroupBy(value => NormalizePhone(value.CustomerPhone)))
        {
            var customer = customers.FirstOrDefault(value => NormalizePhone(value.Phone) == group.Key);
            if (customer is null)
            {
                var first = group.OrderBy(value => value.CreatedAtUtc).First();
                customer = new CafeCustomer
                {
                    Id = Guid.NewGuid(), CommandId = Guid.NewGuid(), Name = first.CustomerName,
                    Kind = "عميل سابق", Phone = group.Key, Address = string.Empty, Active = true, Version = 1,
                    CreatedAtUtc = first.CreatedAtUtc, UpdatedAtUtc = group.Max(value => value.UpdatedAtUtc)
                };
                foreach (var item in catalog)
                    customer.Prices.Add(new CafePrice
                    {
                        Id = Guid.NewGuid(), CafeCustomerId = customer.Id, ItemId = item.Id,
                        UnitPriceMinor = item.RetailPriceMinor, UpdatedAtUtc = customer.UpdatedAtUtc
                    });
                db.CafeCustomers.Add(customer);
                customers.Add(customer);
            }
            foreach (var order in group)
            {
                order.CafeCustomerId = customer.Id;
                foreach (var payment in order.Payments) payment.CafeCustomerId = customer.Id;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string NormalizePhone(string value)
    {
        var normalized = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            if (character == '+' && normalized.Length == 0) { normalized.Append(character); continue; }
            if (!char.IsDigit(character)) continue;
            var digit = (int)char.GetNumericValue(character);
            if (digit is >= 0 and <= 9) normalized.Append((char)('0' + digit));
        }
        return normalized.Length == 0 ? $"legacy-{Guid.NewGuid():N}" : normalized.ToString();
    }

    public async Task<string> CreateUpdateBackupAsync(string reason = "pre-update", CancellationToken cancellationToken = default)
    {
        var databaseDirectory = Path.GetDirectoryName(DatabasePath) ?? ".";
        var backupDirectory = Path.Combine(databaseDirectory, "backups");
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(
            backupDirectory,
            $"{Path.GetFileNameWithoutExtension(DatabasePath)}-{reason}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
        var sourceString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            ForeignKeys = true,
            DefaultTimeout = 5,
            Pooling = false
        }.ToString();
        var destinationString = new SqliteConnectionStringBuilder
        {
            DataSource = backupPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
        await using var source = new SqliteConnection(sourceString);
        await using var destination = new SqliteConnection(destinationString);
        await source.OpenAsync(cancellationToken);
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
        return backupPath;
    }
}
