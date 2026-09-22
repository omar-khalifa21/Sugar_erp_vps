using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace SugarERP.Kitchen;

public sealed class KitchenConfiguration
{
    public int Id { get; set; } = 1;
    public string ApiBaseUrl { get; set; } = string.Empty;
    public Guid SiteId { get; set; }
    public Guid DeviceId { get; set; }
    public string ProtectedCredential { get; set; } = "";
    public int StreamEpoch { get; set; }
    public int NextDeviceSequence { get; set; } = 1;
    public string? Cursor { get; set; }
    public string SiteName { get; set; } = "المطبخ";
    public string PrinterName { get; set; } = "";
    public int NextCustomOrderSequence { get; set; } = 1;
}

public sealed class KitchenProduct { public Guid Id { get; set; } public string Name { get; set; } = ""; public string Unit { get; set; } = ""; public int QuantityScale { get; set; } = 1; public bool Active { get; set; } = true; }
public sealed class KitchenCafeCustomer { public Guid Id { get; set; } public string Name { get; set; } = ""; public string Contact { get; set; } = ""; public string Notes { get; set; } = ""; public bool Active { get; set; } = true; public bool HiddenLocally { get; set; } public List<KitchenCafePrice> Prices { get; set; } = []; }
public sealed class KitchenCafePrice { public Guid CustomerId { get; set; } public Guid ItemId { get; set; } public long UnitPriceMinor { get; set; } public int Version { get; set; } public KitchenCafeCustomer Customer { get; set; } = null!; public KitchenProduct Item { get; set; } = null!; }
public sealed class KitchenCustomOrder
{
    public Guid Id { get; set; } public Guid CustomerId { get; set; } public string OrderNumber { get; set; } = ""; public string CustomerName { get; set; } = ""; public string CustomerPhone { get; set; } = "";
    public string Description { get; set; } = ""; public DateTimeOffset DueAtUtc { get; set; } public long TotalMinor { get; set; } public long PaidMinor { get; set; }
    public string Status { get; set; } = "NEW"; public int Version { get; set; } = 1; public DateTimeOffset CreatedAtUtc { get; set; } public DateTimeOffset UpdatedAtUtc { get; set; }
    public List<KitchenCustomOrderLine> Lines { get; set; } = [];
}
public sealed class KitchenCustomOrderLine { public Guid Id { get; set; } public Guid OrderId { get; set; } public Guid ItemId { get; set; } public string ItemName { get; set; } = ""; public string Unit { get; set; } = ""; public int QuantityScale { get; set; } public long QuantityScaled { get; set; } public long UnitPriceMinor { get; set; } public long LineTotalMinor { get; set; } public KitchenCustomOrder Order { get; set; } = null!; }

public sealed class KitchenRequestRecord
{
    public Guid Id { get; set; }
    public Guid BranchSiteId { get; set; }
    public string BranchName { get; set; } = "";
    public string Status { get; set; } = "REQUESTED";
    public int Version { get; set; }
    public DateTimeOffset SubmittedAtUtc { get; set; }
    public List<KitchenRequestLineRecord> Lines { get; set; } = [];
}

public sealed class KitchenRequestLineRecord
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public Guid ItemId { get; set; }
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public int QuantityScale { get; set; }
    public long RequestedScaled { get; set; }
    public long SentScaled { get; set; }
    public KitchenRequestRecord Request { get; set; } = null!;
}

public sealed class KitchenInbox
{
    public Guid EventId { get; set; }
    public string ContentHash { get; set; } = "";
    public DateTimeOffset AppliedAtUtc { get; set; }
}

public sealed class KitchenOutbox
{
    public Guid EventId { get; set; }
    public Guid RequestId { get; set; }
    public int Sequence { get; set; }
    public string UploadJson { get; set; } = "";
    public bool Acknowledged { get; set; }
    public bool AppliedLocally { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }
    public bool PermanentlyFailed { get; set; }
}
public sealed class KitchenReceiptRecord
{
    public Guid EventId { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid ReceiptId { get; set; }
    public Guid BranchSiteId { get; set; }
    public string Status { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public DateTimeOffset CountedAtUtc { get; set; }
}
public sealed class KitchenReturnRecord
{
    public Guid EventId { get; set; }
    public Guid ReturnId { get; set; }
    public Guid BranchSiteId { get; set; }
    public string Reference { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public DateTimeOffset DispatchedAtUtc { get; set; }
}
public sealed class KitchenIngredientBalance { public Guid ItemId { get; set; } public string Name { get; set; } = ""; public string Unit { get; set; } = ""; public int QuantityScale { get; set; } = 1; public long QuantityScaled { get; set; } public int Version { get; set; } }
public sealed class KitchenRecipeRecord { public Guid ProductItemId { get; set; } public long OutputScaled { get; set; } public int Version { get; set; } public List<KitchenRecipeComponentRecord> Components { get; set; } = []; }
public sealed class KitchenRecipeComponentRecord { public Guid Id { get; set; } public Guid ProductItemId { get; set; } public Guid IngredientItemId { get; set; } public long QuantityScaled { get; set; } public KitchenRecipeRecord Recipe { get; set; } = null!; }
public sealed class KitchenIngredientMovement { public Guid Id { get; set; } public Guid ShipmentId { get; set; } public Guid IngredientItemId { get; set; } public string Kind { get; set; } = "DISPATCH"; public string Reason { get; set; } = ""; public long DeltaScaled { get; set; } public string RecipeSnapshotJson { get; set; } = ""; public DateTimeOffset OccurredAtUtc { get; set; } }

public sealed class KitchenDbContext(DbContextOptions<KitchenDbContext> options) : DbContext(options)
{
    public DbSet<KitchenConfiguration> Configuration => Set<KitchenConfiguration>();
    public DbSet<KitchenRequestRecord> Requests => Set<KitchenRequestRecord>();
    public DbSet<KitchenRequestLineRecord> RequestLines => Set<KitchenRequestLineRecord>();
    public DbSet<KitchenInbox> Inbox => Set<KitchenInbox>();
    public DbSet<KitchenOutbox> Outbox => Set<KitchenOutbox>();
    public DbSet<KitchenReceiptRecord> Receipts => Set<KitchenReceiptRecord>();
    public DbSet<KitchenReturnRecord> Returns => Set<KitchenReturnRecord>();
    public DbSet<KitchenIngredientBalance> Ingredients => Set<KitchenIngredientBalance>();
    public DbSet<KitchenRecipeRecord> Recipes => Set<KitchenRecipeRecord>();
    public DbSet<KitchenRecipeComponentRecord> RecipeComponents => Set<KitchenRecipeComponentRecord>();
    public DbSet<KitchenIngredientMovement> IngredientMovements => Set<KitchenIngredientMovement>();
    public DbSet<KitchenProduct> Products => Set<KitchenProduct>();
    public DbSet<KitchenCafeCustomer> CafeCustomers => Set<KitchenCafeCustomer>();
    public DbSet<KitchenCafePrice> CafePrices => Set<KitchenCafePrice>();
    public DbSet<KitchenCustomOrder> CustomOrders => Set<KitchenCustomOrder>();
    public DbSet<KitchenCustomOrderLine> CustomOrderLines => Set<KitchenCustomOrderLine>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<KitchenConfiguration>().ToTable("configuration").HasKey(x => x.Id);
        model.Entity<KitchenRequestRecord>().ToTable("kitchen_requests").HasKey(x => x.Id);
        model.Entity<KitchenRequestLineRecord>().ToTable("kitchen_request_lines").HasKey(x => x.Id);
        model.Entity<KitchenRequestLineRecord>().HasOne(x => x.Request).WithMany(x => x.Lines).HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<KitchenInbox>().ToTable("inbox").HasKey(x => x.EventId);
        model.Entity<KitchenOutbox>().ToTable("outbox").HasKey(x => x.EventId);
        model.Entity<KitchenOutbox>().HasIndex(x => x.Sequence).IsUnique();
        model.Entity<KitchenReceiptRecord>().ToTable("shipment_receipts").HasKey(x => x.EventId);
        model.Entity<KitchenReceiptRecord>().HasIndex(x => x.ReceiptId).IsUnique();
        model.Entity<KitchenReturnRecord>().ToTable("kitchen_returns").HasKey(x => x.EventId);
        model.Entity<KitchenReturnRecord>().HasIndex(x => x.ReturnId).IsUnique();
        model.Entity<KitchenIngredientBalance>().ToTable("ingredient_balances").HasKey(x => x.ItemId);
        model.Entity<KitchenRecipeRecord>().ToTable("recipes").HasKey(x => x.ProductItemId);
        model.Entity<KitchenRecipeComponentRecord>().ToTable("recipe_components").HasKey(x => x.Id);
        model.Entity<KitchenRecipeComponentRecord>().HasOne(x => x.Recipe).WithMany(x => x.Components).HasForeignKey(x => x.ProductItemId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<KitchenRecipeComponentRecord>().HasIndex(x => new { x.ProductItemId, x.IngredientItemId }).IsUnique();
        model.Entity<KitchenIngredientMovement>().ToTable("ingredient_movements").HasKey(x => x.Id);
        model.Entity<KitchenIngredientMovement>().HasIndex(x => new { x.ShipmentId, x.IngredientItemId }).IsUnique();
        model.Entity<KitchenProduct>().ToTable("products").HasKey(x => x.Id);
        model.Entity<KitchenCafeCustomer>().ToTable("cafe_customers").HasKey(x => x.Id);
        model.Entity<KitchenCafePrice>().ToTable("cafe_prices").HasKey(x => new { x.CustomerId, x.ItemId });
        model.Entity<KitchenCafePrice>().HasOne(x => x.Customer).WithMany(x => x.Prices).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<KitchenCafePrice>().HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<KitchenCustomOrder>().ToTable("custom_orders").HasKey(x => x.Id);
        model.Entity<KitchenCustomOrder>().HasIndex(x => x.OrderNumber).IsUnique();
        model.Entity<KitchenCustomOrderLine>().ToTable("custom_order_lines").HasKey(x => x.Id);
        model.Entity<KitchenCustomOrderLine>().HasOne(x => x.Order).WithMany(x => x.Lines).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class KitchenStore
{
    private readonly string _path;
    private readonly DbContextOptions<KitchenDbContext> _options;
    public SemaphoreSlim WriteLock { get; } = new(1, 1);
    public KitchenStore(string path)
    {
        _path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _options = new DbContextOptionsBuilder<KitchenDbContext>().UseSqlite($"Data Source={path}").Options;
    }
    public async Task<string> CreateUpdateBackupAsync(CancellationToken cancellationToken = default)
    {
        var backupDirectory = Path.Combine(Path.GetDirectoryName(_path)!, "backups");
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(backupDirectory, $"kitchen-pre-update-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
        await using var source = new SqliteConnection($"Data Source={_path};Mode=ReadOnly;Pooling=False");
        await using var destination = new SqliteConnection($"Data Source={backupPath};Mode=ReadWriteCreate;Pooling=False");
        await source.OpenAsync(cancellationToken); await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
        return backupPath;
    }
    public KitchenDbContext Open() => new(_options);
    public async Task InitializeAsync()
    {
        await using var db = Open();
        await db.Database.EnsureCreatedAsync();
        // Additive upgrade for Kitchen 0.1 databases; never replace enrolled data.
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS outbox (EventId TEXT NOT NULL PRIMARY KEY, RequestId TEXT NOT NULL, Sequence INTEGER NOT NULL, UploadJson TEXT NOT NULL, Acknowledged INTEGER NOT NULL DEFAULT 0, AppliedLocally INTEGER NOT NULL DEFAULT 0, Attempts INTEGER NOT NULL DEFAULT 0, NextAttemptAtUtc TEXT NOT NULL DEFAULT '0001-01-01T00:00:00+00:00', LastErrorCode TEXT NULL, LastErrorMessage TEXT NULL, PermanentlyFailed INTEGER NOT NULL DEFAULT 0); CREATE UNIQUE INDEX IF NOT EXISTS IX_outbox_Sequence ON outbox(Sequence);");
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS shipment_receipts (EventId TEXT NOT NULL PRIMARY KEY, ShipmentId TEXT NOT NULL, ReceiptId TEXT NOT NULL, BranchSiteId TEXT NOT NULL, Status TEXT NOT NULL, PayloadJson TEXT NOT NULL, CountedAtUtc TEXT NOT NULL); CREATE UNIQUE INDEX IF NOT EXISTS IX_shipment_receipts_ReceiptId ON shipment_receipts(ReceiptId);");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS kitchen_returns (EventId TEXT NOT NULL PRIMARY KEY, ReturnId TEXT NOT NULL, BranchSiteId TEXT NOT NULL, Reference TEXT NOT NULL, PayloadJson TEXT NOT NULL, DispatchedAtUtc TEXT NOT NULL); CREATE UNIQUE INDEX IF NOT EXISTS IX_kitchen_returns_ReturnId ON kitchen_returns(ReturnId);");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS ingredient_balances (ItemId TEXT NOT NULL PRIMARY KEY, Name TEXT NOT NULL, Unit TEXT NOT NULL, QuantityScale INTEGER NOT NULL, QuantityScaled INTEGER NOT NULL, Version INTEGER NOT NULL); CREATE TABLE IF NOT EXISTS recipes (ProductItemId TEXT NOT NULL PRIMARY KEY, OutputScaled INTEGER NOT NULL, Version INTEGER NOT NULL); CREATE TABLE IF NOT EXISTS recipe_components (Id TEXT NOT NULL PRIMARY KEY, ProductItemId TEXT NOT NULL, IngredientItemId TEXT NOT NULL, QuantityScaled INTEGER NOT NULL, FOREIGN KEY(ProductItemId) REFERENCES recipes(ProductItemId) ON DELETE RESTRICT); CREATE UNIQUE INDEX IF NOT EXISTS IX_recipe_components_ProductItemId_IngredientItemId ON recipe_components(ProductItemId,IngredientItemId); CREATE TABLE IF NOT EXISTS ingredient_movements (Id TEXT NOT NULL PRIMARY KEY, ShipmentId TEXT NOT NULL, IngredientItemId TEXT NOT NULL, Kind TEXT NOT NULL DEFAULT 'DISPATCH', Reason TEXT NOT NULL DEFAULT '', DeltaScaled INTEGER NOT NULL, RecipeSnapshotJson TEXT NOT NULL, OccurredAtUtc TEXT NOT NULL); CREATE UNIQUE INDEX IF NOT EXISTS IX_ingredient_movements_ShipmentId_IngredientItemId ON ingredient_movements(ShipmentId,IngredientItemId);");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS products (Id TEXT NOT NULL PRIMARY KEY, Name TEXT NOT NULL, Unit TEXT NOT NULL, QuantityScale INTEGER NOT NULL, Active INTEGER NOT NULL); CREATE TABLE IF NOT EXISTS cafe_customers (Id TEXT NOT NULL PRIMARY KEY, Name TEXT NOT NULL, Contact TEXT NOT NULL, Notes TEXT NOT NULL, Active INTEGER NOT NULL); CREATE TABLE IF NOT EXISTS cafe_prices (CustomerId TEXT NOT NULL, ItemId TEXT NOT NULL, UnitPriceMinor INTEGER NOT NULL, Version INTEGER NOT NULL, PRIMARY KEY(CustomerId,ItemId), FOREIGN KEY(CustomerId) REFERENCES cafe_customers(Id) ON DELETE RESTRICT, FOREIGN KEY(ItemId) REFERENCES products(Id) ON DELETE RESTRICT); CREATE TABLE IF NOT EXISTS custom_orders (Id TEXT NOT NULL PRIMARY KEY, CustomerId TEXT NOT NULL, OrderNumber TEXT NOT NULL, CustomerName TEXT NOT NULL, CustomerPhone TEXT NOT NULL, Description TEXT NOT NULL, DueAtUtc TEXT NOT NULL, TotalMinor INTEGER NOT NULL, PaidMinor INTEGER NOT NULL, Status TEXT NOT NULL, Version INTEGER NOT NULL, CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL); CREATE UNIQUE INDEX IF NOT EXISTS IX_custom_orders_OrderNumber ON custom_orders(OrderNumber); CREATE TABLE IF NOT EXISTS custom_order_lines (Id TEXT NOT NULL PRIMARY KEY, OrderId TEXT NOT NULL, ItemId TEXT NOT NULL, ItemName TEXT NOT NULL, Unit TEXT NOT NULL, QuantityScale INTEGER NOT NULL, QuantityScaled INTEGER NOT NULL, UnitPriceMinor INTEGER NOT NULL, LineTotalMinor INTEGER NOT NULL, FOREIGN KEY(OrderId) REFERENCES custom_orders(Id) ON DELETE RESTRICT);");
        await AddColumnAsync(db, "ALTER TABLE ingredient_movements ADD COLUMN Kind TEXT NOT NULL DEFAULT 'DISPATCH'");
        await AddColumnAsync(db, "ALTER TABLE ingredient_movements ADD COLUMN Reason TEXT NOT NULL DEFAULT ''");
        await AddColumnAsync(db, "ALTER TABLE configuration ADD COLUMN SiteName TEXT NOT NULL DEFAULT 'المطبخ'");
        await AddColumnAsync(db, "ALTER TABLE configuration ADD COLUMN PrinterName TEXT NOT NULL DEFAULT ''");
        await AddColumnAsync(db, "ALTER TABLE configuration ADD COLUMN NextCustomOrderSequence INTEGER NOT NULL DEFAULT 1");
        await AddColumnAsync(db, "ALTER TABLE outbox ADD COLUMN AppliedLocally INTEGER NOT NULL DEFAULT 0");
        await AddColumnAsync(db, "ALTER TABLE outbox ADD COLUMN Attempts INTEGER NOT NULL DEFAULT 0");
        await AddColumnAsync(db, "ALTER TABLE outbox ADD COLUMN NextAttemptAtUtc TEXT NOT NULL DEFAULT '0001-01-01T00:00:00+00:00'");
        await AddColumnAsync(db, "ALTER TABLE outbox ADD COLUMN LastErrorCode TEXT NULL");
        await AddColumnAsync(db, "ALTER TABLE outbox ADD COLUMN LastErrorMessage TEXT NULL");
        await AddColumnAsync(db, "ALTER TABLE outbox ADD COLUMN PermanentlyFailed INTEGER NOT NULL DEFAULT 0");
        await AddColumnAsync(db, "ALTER TABLE cafe_customers ADD COLUMN HiddenLocally INTEGER NOT NULL DEFAULT 0");
        var configuration = await db.Configuration.SingleOrDefaultAsync();
        var highestSequence = await db.Outbox.Select(x => (int?)x.Sequence).MaxAsync() ?? 0;
        if (configuration is not null && configuration.NextDeviceSequence <= highestSequence)
        {
            configuration.NextDeviceSequence = highestSequence + 1;
            await db.SaveChangesAsync();
        }
    }
    private static async Task AddColumnAsync(KitchenDbContext db, string sql) { try { await db.Database.ExecuteSqlRawAsync(sql); } catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { } }
}
