using Microsoft.EntityFrameworkCore;
using SugarERP.Domain;

namespace SugarERP.Infrastructure.Local;

public sealed class BranchDbContext(DbContextOptions<BranchDbContext> options) : DbContext(options)
{
    public DbSet<DeviceConfiguration> DeviceConfigurations => Set<DeviceConfiguration>();
    public DbSet<BranchLocationBalance> LocationBalances => Set<BranchLocationBalance>();
    public DbSet<BranchInventoryTransaction> InventoryTransactions => Set<BranchInventoryTransaction>();
    public DbSet<BranchInventoryTransactionLine> InventoryTransactionLines => Set<BranchInventoryTransactionLine>();
    public DbSet<CatalogItem> CatalogItems => Set<CatalogItem>();
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<ShiftItemSnapshot> ShiftItemSnapshots => Set<ShiftItemSnapshot>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();
    public DbSet<SalePayment> SalePayments => Set<SalePayment>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<SequenceState> SequenceStates => Set<SequenceState>();
    public DbSet<BranchLocalSettings> BranchLocalSettings => Set<BranchLocalSettings>();
    public DbSet<KitchenRequest> KitchenRequests => Set<KitchenRequest>();
    public DbSet<KitchenRequestLine> KitchenRequestLines => Set<KitchenRequestLine>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentLine> ShipmentLines => Set<ShipmentLine>();
    public DbSet<IncomingReceipt> IncomingReceipts => Set<IncomingReceipt>();
    public DbSet<IncomingReceiptLine> IncomingReceiptLines => Set<IncomingReceiptLine>();
    public DbSet<StockHold> StockHolds => Set<StockHold>();
    public DbSet<QuantityConflict> QuantityConflicts => Set<QuantityConflict>();
    public DbSet<ConflictLine> ConflictLines => Set<ConflictLine>();
    public DbSet<KitchenReturn> KitchenReturns => Set<KitchenReturn>();
    public DbSet<KitchenReturnLine> KitchenReturnLines => Set<KitchenReturnLine>();
    public DbSet<SaleCorrection> SaleCorrections => Set<SaleCorrection>();
    public DbSet<CorrectionLine> CorrectionLines => Set<CorrectionLine>();
    public DbSet<RefundPayment> RefundPayments => Set<RefundPayment>();
    public DbSet<CustomOrder> CustomOrders => Set<CustomOrder>();
    public DbSet<CustomOrderPayment> CustomOrderPayments => Set<CustomOrderPayment>();
    public DbSet<CustomOrderActivity> CustomOrderActivities => Set<CustomOrderActivity>();
    public DbSet<CustomOrderLine> CustomOrderLines => Set<CustomOrderLine>();
    public DbSet<CafeCustomer> CafeCustomers => Set<CafeCustomer>();
    public DbSet<CafePrice> CafePrices => Set<CafePrice>();
    public DbSet<StockCount> StockCounts => Set<StockCount>();
    public DbSet<StockCountLine> StockCountLines => Set<StockCountLine>();
    public DbSet<AdjustmentRequest> AdjustmentRequests => Set<AdjustmentRequest>();
    public DbSet<SideEffectJob> SideEffectJobs => Set<SideEffectJob>();
    public DbSet<ReportArtifact> ReportArtifacts => Set<ReportArtifact>();
    public DbSet<SyncCursor> SyncCursors => Set<SyncCursor>();
    public DbSet<RemoteStockProjection> RemoteStockProjections => Set<RemoteStockProjection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BranchLocationBalance>(entity =>
        {
            entity.ToTable("branch_location_balances", table => table.HasCheckConstraint("ck_location_balance_nonnegative", "QuantityScaled >= 0"));
            entity.HasKey(x => new { x.ItemId, x.Location });
            entity.Property(x => x.Location).HasConversion<string>();
            entity.HasOne<CatalogItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<BranchInventoryTransaction>(entity =>
        {
            entity.ToTable("branch_inventory_transactions");
            entity.Property(x => x.Kind).HasConversion<string>();
            entity.HasIndex(x => new { x.Kind, x.ReferenceId }).IsUnique();
            entity.HasIndex(x => x.OccurredAtUtc);
            entity.HasMany(x => x.Lines).WithOne(x => x.Transaction).HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<BranchInventoryTransactionLine>(entity =>
        {
            entity.ToTable("branch_inventory_transaction_lines", table => table.HasCheckConstraint("ck_inventory_delta_nonzero", "DeltaScaled <> 0"));
            entity.Property(x => x.Location).HasConversion<string>();
            entity.HasIndex(x => new { x.TransactionId, x.ItemId, x.Location }).IsUnique();
            entity.HasOne<CatalogItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<DeviceConfiguration>(entity =>
        {
            entity.ToTable("device_configuration", table =>
            {
                table.HasCheckConstraint("ck_device_configuration_singleton", "\"Id\" = 1");
                table.HasCheckConstraint("ck_device_configuration_type1", "\"Profile\" IN ('BranchType1', 'BranchType2')");
            });
            entity.Property(value => value.Profile).HasConversion<string>();
            entity.Property(value => value.SiteName).HasMaxLength(200);
            entity.Property(value => value.ApiBaseUrl).HasMaxLength(500);
            entity.Property(value => value.DeviceCredential).HasMaxLength(500);
        });

        modelBuilder.Entity<CatalogItem>(entity =>
        {
            entity.ToTable("catalog_items", table =>
            {
                table.HasCheckConstraint("ck_catalog_item_scale", "\"QuantityScale\" > 0");
                table.HasCheckConstraint("ck_catalog_item_price", "\"RetailPriceMinor\" >= 0");
                table.HasCheckConstraint("ck_catalog_item_version", "\"Version\" > 0");
            });
            entity.HasIndex(value => value.Sku).IsUnique();
            entity.Property(value => value.Sku).HasMaxLength(100);
            entity.Property(value => value.NameAr).HasMaxLength(300);
            entity.Property(value => value.Unit).HasMaxLength(30);
        });

        modelBuilder.Entity<StockBalance>(entity =>
        {
            entity.ToTable("stock_balances", table =>
            {
                table.HasCheckConstraint("ck_stock_balance_nonnegative", "\"QuantityScaled\" >= 0");
                table.HasCheckConstraint("ck_stock_balance_revision", "\"Revision\" > 0");
            });
            entity.HasKey(value => value.ItemId);
            entity.HasOne(value => value.Item).WithOne(value => value.StockBalance).HasForeignKey<StockBalance>(value => value.ItemId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Shift>(entity =>
        {
            entity.ToTable("shifts", table => table.HasCheckConstraint("ck_shift_cash", "\"OpeningCashMinor\" >= 0"));
            entity.Property(value => value.Kind).HasConversion<string>();
            entity.Property(value => value.Status).HasConversion<string>();
            entity.Property(value => value.BusinessDate).HasMaxLength(10);
            entity.HasIndex(value => value.Status).IsUnique().HasFilter("\"Status\" = 'Open'");
            entity.HasIndex(value => new { value.BusinessDate, value.Kind });
        });

        modelBuilder.Entity<ShiftItemSnapshot>(entity =>
        {
            entity.ToTable("shift_item_snapshots");
            entity.HasKey(value => new { value.ShiftId, value.ItemId });
            entity.Property(value => value.NameSnapshot).HasMaxLength(300);
            entity.Property(value => value.UnitSnapshot).HasMaxLength(30);
            entity.HasOne(value => value.Shift).WithMany(value => value.OpeningItems).HasForeignKey(value => value.ShiftId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Sale>(entity =>
        {
            entity.ToTable("sales", table =>
            {
                table.HasCheckConstraint("ck_sale_money", "\"SubtotalMinor\" >= 0 AND \"DiscountMinor\" >= 0 AND \"TipMinor\" >= 0 AND \"TotalMinor\" >= 0");
                table.HasCheckConstraint("ck_sale_total", "\"TotalMinor\" = \"SubtotalMinor\" - \"DiscountMinor\" + \"TipMinor\"");
            });
            entity.HasIndex(value => value.CommandId).IsUnique();
            entity.HasIndex(value => value.ReceiptNumber).IsUnique();
            entity.HasIndex(value => new { value.BusinessDate, value.OccurredAtUtc });
            entity.Property(value => value.ReceiptNumber).HasMaxLength(100);
            entity.Property(value => value.BusinessDate).HasMaxLength(10);
            entity.Property(value => value.PaymentMethod).HasConversion<string>();
            entity.Property(value => value.Fulfillment).HasConversion<string>();
            entity.Property(value => value.Status).HasConversion<string>();
            entity.HasOne(value => value.Shift).WithMany(value => value.Sales).HasForeignKey(value => value.ShiftId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SaleLine>(entity =>
        {
            entity.ToTable("sale_lines", table =>
            {
                table.HasCheckConstraint("ck_sale_line_quantity", "\"QuantityScaled\" > 0 AND \"QuantityScale\" > 0");
                table.HasCheckConstraint("ck_sale_line_money", "\"UnitPriceMinor\" >= 0 AND \"AllocatedDiscountMinor\" >= 0 AND \"TotalMinor\" >= 0");
            });
            entity.HasIndex(value => new { value.SaleId, value.ItemId }).IsUnique();
            entity.Property(value => value.NameSnapshot).HasMaxLength(300);
            entity.Property(value => value.SkuSnapshot).HasMaxLength(100);
            entity.Property(value => value.UnitSnapshot).HasMaxLength(30);
            entity.HasOne(value => value.Sale).WithMany(value => value.Lines).HasForeignKey(value => value.SaleId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SalePayment>(entity =>
        {
            entity.ToTable("sale_payments", table => table.HasCheckConstraint("ck_sale_payment_amount", "\"AmountMinor\" >= 0"));
            entity.Property(value => value.Method).HasConversion<string>();
            entity.Property(value => value.Reference).HasMaxLength(200);
            entity.HasOne(value => value.Sale).WithMany(value => value.Payments).HasForeignKey(value => value.SaleId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockMovement>(entity =>
        {
            entity.ToTable("stock_movements", table => table.HasCheckConstraint("ck_stock_movement_nonzero", "\"DeltaScaled\" <> 0"));
            entity.Property(value => value.Kind).HasConversion<string>();
            entity.HasIndex(value => new { value.SourceLineId, value.Kind }).IsUnique();
            entity.HasIndex(value => new { value.ItemId, value.OccurredAtUtc });
        });

        modelBuilder.Entity<CashMovement>(entity =>
        {
            entity.ToTable("cash_movements");
            entity.Property(value => value.Kind).HasMaxLength(50);
            entity.HasIndex(value => new { value.SourceId, value.Kind }).IsUnique();
            entity.HasIndex(value => new { value.ShiftId, value.OccurredAtUtc });
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("sync_outbox", table => table.HasCheckConstraint("ck_outbox_sequence", "\"DeviceSequence\" > 0"));
            entity.HasKey(value => value.EventId);
            entity.HasIndex(value => value.DeviceSequence).IsUnique();
            entity.HasIndex(value => new { value.State, value.NextAttemptAtUtc });
            entity.Property(value => value.EventType).HasMaxLength(120);
            entity.Property(value => value.ContentHash).HasMaxLength(64);
            entity.Property(value => value.LastErrorCode).HasMaxLength(100);
            entity.Property(value => value.State).HasConversion<string>();
        });

        modelBuilder.Entity<InboxMessage>(entity =>
        {
            entity.ToTable("sync_inbox");
            entity.HasKey(value => value.EventId);
            entity.Property(value => value.ContentHash).HasMaxLength(64);
        });

        modelBuilder.Entity<SequenceState>(entity =>
        {
            entity.ToTable("sequence_state", table =>
            {
                table.HasCheckConstraint("ck_sequence_singleton", "\"Id\" = 1");
                table.HasCheckConstraint("ck_sequence_positive", "\"NextDeviceSequence\" > 0 AND \"NextReceiptSequence\" > 0 AND \"NextCustomOrderSequence\" > 0");
            });
        });

        ConfigureBranchModules(modelBuilder);
    }

    private static void ConfigureBranchModules(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BranchLocalSettings>(entity =>
        {
            entity.ToTable("branch_local_settings", table =>
                table.HasCheckConstraint("ck_branch_settings_singleton", "\"Id\" = 1"));
            entity.Property(value => value.ExportDirectory).HasMaxLength(1000);
            entity.Property(value => value.PrinterName).HasMaxLength(300);
        });

        modelBuilder.Entity<KitchenRequest>(entity =>
        {
            entity.ToTable("kitchen_requests", table =>
                table.HasCheckConstraint("ck_kitchen_request_version", "\"Version\" > 0"));
            entity.HasIndex(value => value.CommandId).IsUnique();
            entity.HasIndex(value => new { value.BusinessDate, value.RequestedAtUtc });
            entity.Property(value => value.Status).HasConversion<string>();
            entity.Property(value => value.BusinessDate).HasMaxLength(10);
        });
        modelBuilder.Entity<KitchenRequestLine>(entity =>
        {
            entity.ToTable("kitchen_request_lines", table =>
                table.HasCheckConstraint("ck_kitchen_request_line_quantity", "\"RequestedScaled\" > 0 AND \"QuantityScale\" > 0 AND \"SentScaled\" >= 0"));
            entity.HasIndex(value => new { value.RequestId, value.ItemId }).IsUnique();
            entity.Property(value => value.NameSnapshot).HasMaxLength(300);
            entity.Property(value => value.UnitSnapshot).HasMaxLength(30);
            entity.HasOne(value => value.Request).WithMany(value => value.Lines).HasForeignKey(value => value.RequestId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Shipment>(entity =>
        {
            entity.ToTable("shipments", table =>
                table.HasCheckConstraint("ck_shipment_version", "\"Version\" > 0"));
            entity.HasIndex(value => value.CommandId).IsUnique();
            entity.HasIndex(value => value.Reference).IsUnique();
            entity.HasIndex(value => new { value.Status, value.DispatchedAtUtc });
            entity.Property(value => value.Status).HasConversion<string>();
            entity.Property(value => value.Reference).HasMaxLength(120);
        });
        modelBuilder.Entity<ShipmentLine>(entity =>
        {
            entity.ToTable("shipment_lines", table =>
                table.HasCheckConstraint("ck_shipment_line_quantity", "\"SentScaled\" > 0 AND \"QuantityScale\" > 0"));
            entity.HasIndex(value => new { value.ShipmentId, value.ItemId }).IsUnique();
            entity.HasIndex(value => value.RequestLineId);
            entity.Property(value => value.NameSnapshot).HasMaxLength(300);
            entity.Property(value => value.UnitSnapshot).HasMaxLength(30);
            entity.HasOne(value => value.Shipment).WithMany(value => value.Lines).HasForeignKey(value => value.ShipmentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IncomingReceipt>(entity =>
        {
            entity.ToTable("incoming_receipts");
            entity.HasIndex(value => value.CommandId).IsUnique();
            entity.HasIndex(value => value.ShipmentId).IsUnique();
            entity.Property(value => value.Status).HasConversion<string>();
            entity.HasOne(value => value.Shipment).WithOne(value => value.Receipt).HasForeignKey<IncomingReceipt>(value => value.ShipmentId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<IncomingReceiptLine>(entity =>
        {
            entity.ToTable("incoming_receipt_lines", table =>
                table.HasCheckConstraint("ck_receipt_line_quantity", "\"SentScaled\" > 0 AND \"CountedScaled\" >= 0 AND (\"ConfirmedScaled\" IS NULL OR \"ConfirmedScaled\" >= 0)"));
            entity.HasIndex(value => value.ShipmentLineId).IsUnique();
            entity.HasIndex(value => new { value.ReceiptId, value.ItemId }).IsUnique();
            entity.HasOne(value => value.Receipt).WithMany(value => value.Lines).HasForeignKey(value => value.ReceiptId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockHold>(entity =>
        {
            entity.ToTable("stock_holds", table =>
                table.HasCheckConstraint("ck_stock_hold_quantity", "\"PhysicalCountScaled\" >= 0 AND \"ReleasedScaled\" >= 0 AND \"ReleasedScaled\" <= \"PhysicalCountScaled\""));
            entity.HasIndex(value => value.ReceiptLineId).IsUnique();
            entity.HasIndex(value => new { value.Status, value.ItemId });
            entity.Property(value => value.Status).HasConversion<string>();
        });

        modelBuilder.Entity<QuantityConflict>(entity =>
        {
            entity.ToTable("quantity_conflicts");
            entity.HasIndex(value => value.ReceiptId).IsUnique();
            entity.Property(value => value.Status).HasMaxLength(50);
        });
        modelBuilder.Entity<ConflictLine>(entity =>
        {
            entity.ToTable("conflict_lines", table =>
                table.HasCheckConstraint("ck_conflict_line_quantity", "\"SentScaled\" >= 0 AND \"CountedScaled\" >= 0"));
            entity.HasIndex(value => value.ReceiptLineId).IsUnique();
            entity.Property(value => value.Note).HasMaxLength(500);
            entity.HasOne(value => value.Conflict).WithMany(value => value.Lines).HasForeignKey(value => value.ConflictId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<KitchenReturn>(entity =>
        {
            entity.ToTable("kitchen_returns");
            entity.HasIndex(value => value.CommandId).IsUnique();
            entity.HasIndex(value => value.Reference).IsUnique();
            entity.HasIndex(value => new { value.ShiftId, value.DispatchedAtUtc });
            entity.Property(value => value.Status).HasConversion<string>();
            entity.Property(value => value.Reference).HasMaxLength(120);
        });
        modelBuilder.Entity<KitchenReturnLine>(entity =>
        {
            entity.ToTable("kitchen_return_lines", table =>
                table.HasCheckConstraint("ck_kitchen_return_line_quantity", "\"SentScaled\" > 0 AND \"QuantityScale\" > 0"));
            entity.HasIndex(value => new { value.ReturnId, value.ItemId }).IsUnique();
            entity.Property(value => value.NameSnapshot).HasMaxLength(300);
            entity.Property(value => value.UnitSnapshot).HasMaxLength(30);
            entity.HasOne(value => value.Return).WithMany(value => value.Lines).HasForeignKey(value => value.ReturnId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SaleCorrection>(entity =>
        {
            entity.ToTable("sale_corrections", table =>
                table.HasCheckConstraint("ck_sale_correction_refund", "\"RefundTotalMinor\" > 0"));
            entity.HasIndex(value => value.CommandId).IsUnique();
            entity.HasIndex(value => new { value.OriginalSaleId, value.OccurredAtUtc });
            entity.Property(value => value.Kind).HasConversion<string>();
            entity.Property(value => value.RefundMethod).HasConversion<string>();
            entity.Property(value => value.Reason).HasMaxLength(500);
            entity.Property(value => value.Actor).HasMaxLength(200);
        });
        modelBuilder.Entity<CorrectionLine>(entity =>
        {
            entity.ToTable("correction_lines", table =>
                table.HasCheckConstraint("ck_correction_line_quantity", "\"QuantityDeltaScaled\" < 0 AND \"AmountDeltaMinor\" < 0 AND \"RestockScaled\" >= 0 AND \"QuantityScale\" > 0"));
            entity.HasIndex(value => new { value.CorrectionId, value.OriginalSaleLineId }).IsUnique();
            entity.Property(value => value.Disposition).HasConversion<string>();
            entity.Property(value => value.NameSnapshot).HasMaxLength(300);
            entity.Property(value => value.UnitSnapshot).HasMaxLength(30);
            entity.HasOne(value => value.Correction).WithMany(value => value.Lines).HasForeignKey(value => value.CorrectionId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<RefundPayment>(entity =>
        {
            entity.ToTable("refund_payments", table =>
                table.HasCheckConstraint("ck_refund_payment_amount", "\"AmountMinor\" > 0"));
            entity.HasIndex(value => value.CorrectionId).IsUnique();
            entity.Property(value => value.Method).HasConversion<string>();
            entity.Property(value => value.Reference).HasMaxLength(200);
            entity.HasOne(value => value.Correction).WithOne(value => value.Payment).HasForeignKey<RefundPayment>(value => value.CorrectionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CafeCustomer>(entity =>
        {
            entity.ToTable("cafe_customers", table => table.HasCheckConstraint("ck_cafe_customer_version", "\"Version\" > 0"));
            entity.HasIndex(value => value.CommandId).IsUnique();
            entity.HasIndex(value => value.Phone).IsUnique();
            entity.Property(value => value.Name).HasMaxLength(200);
            entity.Property(value => value.Kind).HasMaxLength(50);
            entity.Property(value => value.Phone).HasMaxLength(50);
            entity.Property(value => value.Address).HasMaxLength(500);
        });
        modelBuilder.Entity<CafePrice>(entity =>
        {
            entity.ToTable("cafe_prices", table => table.HasCheckConstraint("ck_cafe_price_nonnegative", "\"UnitPriceMinor\" >= 0"));
            entity.HasIndex(value => new { value.CafeCustomerId, value.ItemId }).IsUnique();
            entity.HasOne(value => value.Customer).WithMany(value => value.Prices).HasForeignKey(value => value.CafeCustomerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(value => value.Item).WithMany().HasForeignKey(value => value.ItemId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<CustomOrder>(entity =>
        {
            entity.ToTable("custom_orders", table =>
            {
                table.HasCheckConstraint("ck_custom_order_money", "\"TotalMinor\" >= 0");
                table.HasCheckConstraint("ck_custom_order_version", "\"Version\" > 0");
            });
            entity.HasIndex(value => value.CommandId).IsUnique();
            entity.HasIndex(value => value.OrderNumber).IsUnique();
            entity.HasIndex(value => new { value.Status, value.DueAtUtc });
            entity.Property(value => value.OrderNumber).HasMaxLength(100);
            entity.Property(value => value.CustomerName).HasMaxLength(200);
            entity.Property(value => value.CustomerPhone).HasMaxLength(50);
            entity.Property(value => value.Description).HasMaxLength(2000);
            entity.Property(value => value.Status).HasConversion<string>();
            entity.HasOne(value => value.CafeCustomer).WithMany(value => value.Orders).HasForeignKey(value => value.CafeCustomerId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<CustomOrderLine>(entity =>
        {
            entity.ToTable("custom_order_lines", table => table.HasCheckConstraint("ck_custom_order_line_values", "\"QuantityScaled\" > 0 AND \"QuantityScale\" > 0 AND \"UnitPriceMinor\" >= 0 AND \"LineTotalMinor\" >= 0"));
            entity.HasIndex(value => new { value.CustomOrderId, value.ItemId }).IsUnique();
            entity.Property(value => value.ItemNameSnapshot).HasMaxLength(300);
            entity.Property(value => value.UnitSnapshot).HasMaxLength(30);
            entity.HasOne(value => value.Order).WithMany(value => value.Lines).HasForeignKey(value => value.CustomOrderId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<CustomOrderPayment>(entity =>
        {
            entity.ToTable("custom_order_payments", table =>
                table.HasCheckConstraint("ck_custom_order_payment_amount", "\"AmountMinor\" > 0"));
            entity.HasIndex(value => value.CommandId).IsUnique();
            entity.HasIndex(value => new { value.CustomOrderId, value.PaidAtUtc });
            entity.HasIndex(value => new { value.CafeCustomerId, value.PaidAtUtc });
            entity.Property(value => value.Method).HasConversion<string>();
            entity.HasOne(value => value.Order).WithMany(value => value.Payments).HasForeignKey(value => value.CustomOrderId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CafeCustomer>().WithMany().HasForeignKey(value => value.CafeCustomerId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<CustomOrderActivity>(entity =>
        {
            entity.ToTable("custom_order_activities");
            entity.HasIndex(value => value.CommandId).IsUnique();
            entity.HasIndex(value => new { value.CustomOrderId, value.OccurredAtUtc });
            entity.Property(value => value.Kind).HasMaxLength(50);
            entity.Property(value => value.Note).HasMaxLength(500);
            entity.HasOne(value => value.Order).WithMany(value => value.Activities).HasForeignKey(value => value.CustomOrderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockCount>(entity =>
        {
            entity.ToTable("stock_counts");
            entity.HasIndex(value => value.CommandId).IsUnique();
            entity.HasIndex(value => value.ShiftId).IsUnique();
            entity.Property(value => value.Status).HasConversion<string>();
        });
        modelBuilder.Entity<StockCountLine>(entity =>
        {
            entity.ToTable("stock_count_lines", table =>
                table.HasCheckConstraint("ck_stock_count_line_quantity", "\"ExpectedScaled\" >= 0 AND \"ActualScaled\" >= 0 AND \"QuantityScale\" > 0 AND \"DifferenceScaled\" = \"ActualScaled\" - \"ExpectedScaled\""));
            entity.HasIndex(value => new { value.CountId, value.ItemId }).IsUnique();
            entity.Property(value => value.NameSnapshot).HasMaxLength(300);
            entity.Property(value => value.UnitSnapshot).HasMaxLength(30);
            entity.HasOne(value => value.Count).WithMany(value => value.Lines).HasForeignKey(value => value.CountId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<AdjustmentRequest>(entity =>
        {
            entity.ToTable("adjustment_requests", table =>
                table.HasCheckConstraint("ck_adjustment_request_nonzero", "\"ProposedDeltaScaled\" <> 0"));
            entity.HasIndex(value => value.CountLineId).IsUnique();
            entity.Property(value => value.Status).HasConversion<string>();
            entity.Property(value => value.Reason).HasMaxLength(500);
        });

        modelBuilder.Entity<SideEffectJob>(entity =>
        {
            entity.ToTable("side_effect_jobs", table =>
                table.HasCheckConstraint("ck_side_effect_job_version", "\"DocumentVersion\" > 0 AND \"Attempts\" >= 0"));
            entity.HasIndex(value => new { value.SourceId, value.Kind, value.DocumentVersion }).IsUnique();
            entity.HasIndex(value => new { value.State, value.NextAttemptAtUtc });
            entity.Property(value => value.Kind).HasConversion<string>();
            entity.Property(value => value.State).HasConversion<string>();
            entity.Property(value => value.LastError).HasMaxLength(1000);
        });
        modelBuilder.Entity<ReportArtifact>(entity =>
        {
            entity.ToTable("report_artifacts", table =>
                table.HasCheckConstraint("ck_report_artifact", "\"ReportVersion\" > 0 AND \"Format\" = 'xlsx' AND \"ByteLength\" >= 0"));
            entity.HasIndex(value => new { value.ShiftId, value.ReportVersion }).IsUnique();
            entity.Property(value => value.ContentHash).HasMaxLength(64);
            entity.Property(value => value.Format).HasMaxLength(10);
            entity.Property(value => value.LocalPath).HasMaxLength(1000);
            entity.Property(value => value.Error).HasMaxLength(1000);
        });

        modelBuilder.Entity<SyncCursor>(entity =>
        {
            entity.ToTable("sync_cursors");
            entity.HasKey(value => value.FeedScope);
            entity.Property(value => value.FeedScope).HasMaxLength(100);
            entity.Property(value => value.Cursor).HasMaxLength(2000);
            entity.Property(value => value.SnapshotVersion).HasMaxLength(200);
        });
        modelBuilder.Entity<RemoteStockProjection>(entity =>
        {
            entity.ToTable("remote_stock_projections", table =>
                table.HasCheckConstraint("ck_remote_stock_scale", "\"QuantityScale\" > 0"));
            entity.HasKey(value => new { value.SiteId, value.ItemId });
            entity.Property(value => value.SiteName).HasMaxLength(200);
            entity.Property(value => value.ItemName).HasMaxLength(300);
            entity.Property(value => value.Unit).HasMaxLength(30);
        });
    }
}
