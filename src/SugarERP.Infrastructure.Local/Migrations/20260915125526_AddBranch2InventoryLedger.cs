using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SugarERP.Infrastructure.Local.Migrations
{
    /// <inheritdoc />
    public partial class AddBranch2InventoryLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_device_configuration_type1",
                table: "device_configuration");

            migrationBuilder.CreateTable(
                name: "branch_inventory_transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branch_inventory_transactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "branch_location_balances",
                columns: table => new
                {
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Location = table.Column<string>(type: "TEXT", nullable: false),
                    QuantityScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branch_location_balances", x => new { x.ItemId, x.Location });
                    table.CheckConstraint("ck_location_balance_nonnegative", "QuantityScaled >= 0");
                    table.ForeignKey(
                        name: "FK_branch_location_balances_catalog_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "catalog_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "branch_inventory_transaction_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TransactionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Location = table.Column<string>(type: "TEXT", nullable: false),
                    DeltaScaled = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branch_inventory_transaction_lines", x => x.Id);
                    table.CheckConstraint("ck_inventory_delta_nonzero", "DeltaScaled <> 0");
                    table.ForeignKey(
                        name: "FK_branch_inventory_transaction_lines_branch_inventory_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "branch_inventory_transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_branch_inventory_transaction_lines_catalog_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "catalog_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_configuration_type1",
                table: "device_configuration",
                sql: "\"Profile\" IN ('BranchType1', 'BranchType2')");

            migrationBuilder.CreateIndex(
                name: "IX_branch_inventory_transaction_lines_ItemId",
                table: "branch_inventory_transaction_lines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_branch_inventory_transaction_lines_TransactionId_ItemId_Location",
                table: "branch_inventory_transaction_lines",
                columns: new[] { "TransactionId", "ItemId", "Location" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_branch_inventory_transactions_Kind_ReferenceId",
                table: "branch_inventory_transactions",
                columns: new[] { "Kind", "ReferenceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_branch_inventory_transactions_OccurredAtUtc",
                table: "branch_inventory_transactions",
                column: "OccurredAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "branch_inventory_transaction_lines");

            migrationBuilder.DropTable(
                name: "branch_location_balances");

            migrationBuilder.DropTable(
                name: "branch_inventory_transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_configuration_type1",
                table: "device_configuration");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_configuration_type1",
                table: "device_configuration",
                sql: "\"Profile\" = 'BranchType1'");
        }
    }
}
