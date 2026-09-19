using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SugarERP.Infrastructure.Local.Migrations
{
    /// <inheritdoc />
    public partial class InitialBranchType1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cash_movements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShiftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_movements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "catalog_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sku = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    NameAr = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    QuantityScale = table.Column<int>(type: "INTEGER", nullable: false),
                    RetailPriceMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_items", x => x.Id);
                    table.CheckConstraint("ck_catalog_item_price", "\"RetailPriceMinor\" >= 0");
                    table.CheckConstraint("ck_catalog_item_scale", "\"QuantityScale\" > 0");
                    table.CheckConstraint("ck_catalog_item_version", "\"Version\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "device_configuration",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Profile = table.Column<string>(type: "TEXT", nullable: false),
                    SiteName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ApiBaseUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    DeviceCredential = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    StreamEpoch = table.Column<int>(type: "INTEGER", nullable: false),
                    TouchMode = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnrolledAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_configuration", x => x.Id);
                    table.CheckConstraint("ck_device_configuration_singleton", "\"Id\" = 1");
                    table.CheckConstraint("ck_device_configuration_type1", "\"Profile\" = 'BranchType1'");
                });

            migrationBuilder.CreateTable(
                name: "sequence_state",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NextDeviceSequence = table.Column<int>(type: "INTEGER", nullable: false),
                    NextReceiptSequence = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sequence_state", x => x.Id);
                    table.CheckConstraint("ck_sequence_positive", "\"NextDeviceSequence\" > 0 AND \"NextReceiptSequence\" > 0");
                    table.CheckConstraint("ck_sequence_singleton", "\"Id\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "shifts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    BusinessDate = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    OpenedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    OpeningCashMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpectedCashMinor = table.Column<long>(type: "INTEGER", nullable: true),
                    ActualCashMinor = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shifts", x => x.Id);
                    table.CheckConstraint("ck_shift_cash", "\"OpeningCashMinor\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShiftId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    DeltaScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_movements", x => x.Id);
                    table.CheckConstraint("ck_stock_movement_nonzero", "\"DeltaScaled\" <> 0");
                });

            migrationBuilder.CreateTable(
                name: "sync_inbox",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AppliedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_inbox", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "sync_outbox",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AggregateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceSequence = table.Column<int>(type: "INTEGER", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    DependenciesJson = table.Column<string>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    AcknowledgedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastErrorCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_outbox", x => x.EventId);
                    table.CheckConstraint("ck_outbox_sequence", "\"DeviceSequence\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "stock_balances",
                columns: table => new
                {
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuantityScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    AsOfUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_balances", x => x.ItemId);
                    table.CheckConstraint("ck_stock_balance_nonnegative", "\"QuantityScaled\" >= 0");
                    table.CheckConstraint("ck_stock_balance_revision", "\"Revision\" > 0");
                    table.ForeignKey(
                        name: "FK_stock_balances_catalog_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "catalog_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommandId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShiftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReceiptNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    BusinessDate = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    PaymentMethod = table.Column<string>(type: "TEXT", nullable: false),
                    Fulfillment = table.Column<string>(type: "TEXT", nullable: false),
                    SubtotalMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    DiscountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    TipMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales", x => x.Id);
                    table.CheckConstraint("ck_sale_money", "\"SubtotalMinor\" >= 0 AND \"DiscountMinor\" >= 0 AND \"TipMinor\" >= 0 AND \"TotalMinor\" >= 0");
                    table.CheckConstraint("ck_sale_total", "\"TotalMinor\" = \"SubtotalMinor\" - \"DiscountMinor\" + \"TipMinor\"");
                    table.ForeignKey(
                        name: "FK_sales_shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shift_item_snapshots",
                columns: table => new
                {
                    ShiftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NameSnapshot = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    UnitSnapshot = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    QuantityScale = table.Column<int>(type: "INTEGER", nullable: false),
                    OpeningQuantityScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    IncomingScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    SoldScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    KitchenReturnScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    WasteScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    AdjustmentScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpectedCloseScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    ActualCloseScaled = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shift_item_snapshots", x => new { x.ShiftId, x.ItemId });
                    table.ForeignKey(
                        name: "FK_shift_item_snapshots_shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SaleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuantityScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    QuantityScale = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitPriceMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    AllocatedDiscountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    NameSnapshot = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    SkuSnapshot = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    UnitSnapshot = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_lines", x => x.Id);
                    table.CheckConstraint("ck_sale_line_money", "\"UnitPriceMinor\" >= 0 AND \"AllocatedDiscountMinor\" >= 0 AND \"TotalMinor\" >= 0");
                    table.CheckConstraint("ck_sale_line_quantity", "\"QuantityScaled\" > 0 AND \"QuantityScale\" > 0");
                    table.ForeignKey(
                        name: "FK_sale_lines_sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SaleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Method = table.Column<string>(type: "TEXT", nullable: false),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    PaidAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_payments", x => x.Id);
                    table.CheckConstraint("ck_sale_payment_amount", "\"AmountMinor\" >= 0");
                    table.ForeignKey(
                        name: "FK_sale_payments_sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cash_movements_ShiftId_OccurredAtUtc",
                table: "cash_movements",
                columns: new[] { "ShiftId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_cash_movements_SourceId_Kind",
                table: "cash_movements",
                columns: new[] { "SourceId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_catalog_items_Sku",
                table: "catalog_items",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sale_lines_SaleId_ItemId",
                table: "sale_lines",
                columns: new[] { "SaleId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sale_payments_SaleId",
                table: "sale_payments",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_sales_BusinessDate_OccurredAtUtc",
                table: "sales",
                columns: new[] { "BusinessDate", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_CommandId",
                table: "sales",
                column: "CommandId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_ReceiptNumber",
                table: "sales",
                column: "ReceiptNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_ShiftId",
                table: "sales",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_shifts_BusinessDate_Kind",
                table: "shifts",
                columns: new[] { "BusinessDate", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_shifts_Status",
                table: "shifts",
                column: "Status",
                unique: true,
                filter: "\"Status\" = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_ItemId_OccurredAtUtc",
                table: "stock_movements",
                columns: new[] { "ItemId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_SourceLineId_Kind",
                table: "stock_movements",
                columns: new[] { "SourceLineId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sync_outbox_DeviceSequence",
                table: "sync_outbox",
                column: "DeviceSequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sync_outbox_State_NextAttemptAtUtc",
                table: "sync_outbox",
                columns: new[] { "State", "NextAttemptAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cash_movements");

            migrationBuilder.DropTable(
                name: "device_configuration");

            migrationBuilder.DropTable(
                name: "sale_lines");

            migrationBuilder.DropTable(
                name: "sale_payments");

            migrationBuilder.DropTable(
                name: "sequence_state");

            migrationBuilder.DropTable(
                name: "shift_item_snapshots");

            migrationBuilder.DropTable(
                name: "stock_balances");

            migrationBuilder.DropTable(
                name: "stock_movements");

            migrationBuilder.DropTable(
                name: "sync_inbox");

            migrationBuilder.DropTable(
                name: "sync_outbox");

            migrationBuilder.DropTable(
                name: "sales");

            migrationBuilder.DropTable(
                name: "catalog_items");

            migrationBuilder.DropTable(
                name: "shifts");
        }
    }
}
