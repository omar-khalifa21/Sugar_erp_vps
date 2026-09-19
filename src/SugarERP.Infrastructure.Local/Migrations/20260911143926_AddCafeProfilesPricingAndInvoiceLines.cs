using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SugarERP.Infrastructure.Local.Migrations
{
    /// <inheritdoc />
    public partial class AddCafeProfilesPricingAndInvoiceLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CafeCustomerId",
                table: "custom_orders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CafeCustomerId",
                table: "custom_order_payments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cafe_customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommandId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cafe_customers", x => x.Id);
                    table.CheckConstraint("ck_cafe_customer_version", "\"Version\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "custom_order_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemNameSnapshot = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    UnitSnapshot = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    QuantityScale = table.Column<int>(type: "INTEGER", nullable: false),
                    QuantityScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    UnitPriceMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    LineTotalMinor = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_order_lines", x => x.Id);
                    table.CheckConstraint("ck_custom_order_line_values", "\"QuantityScaled\" > 0 AND \"QuantityScale\" > 0 AND \"UnitPriceMinor\" >= 0 AND \"LineTotalMinor\" >= 0");
                    table.ForeignKey(
                        name: "FK_custom_order_lines_custom_orders_CustomOrderId",
                        column: x => x.CustomOrderId,
                        principalTable: "custom_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cafe_prices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CafeCustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UnitPriceMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cafe_prices", x => x.Id);
                    table.CheckConstraint("ck_cafe_price_nonnegative", "\"UnitPriceMinor\" >= 0");
                    table.ForeignKey(
                        name: "FK_cafe_prices_cafe_customers_CafeCustomerId",
                        column: x => x.CafeCustomerId,
                        principalTable: "cafe_customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cafe_prices_catalog_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "catalog_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_custom_orders_CafeCustomerId",
                table: "custom_orders",
                column: "CafeCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_custom_order_payments_CafeCustomerId_PaidAtUtc",
                table: "custom_order_payments",
                columns: new[] { "CafeCustomerId", "PaidAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_cafe_customers_CommandId",
                table: "cafe_customers",
                column: "CommandId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cafe_customers_Phone",
                table: "cafe_customers",
                column: "Phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cafe_prices_CafeCustomerId_ItemId",
                table: "cafe_prices",
                columns: new[] { "CafeCustomerId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cafe_prices_ItemId",
                table: "cafe_prices",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_custom_order_lines_CustomOrderId_ItemId",
                table: "custom_order_lines",
                columns: new[] { "CustomOrderId", "ItemId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_custom_order_payments_cafe_customers_CafeCustomerId",
                table: "custom_order_payments",
                column: "CafeCustomerId",
                principalTable: "cafe_customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_custom_orders_cafe_customers_CafeCustomerId",
                table: "custom_orders",
                column: "CafeCustomerId",
                principalTable: "cafe_customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_custom_order_payments_cafe_customers_CafeCustomerId",
                table: "custom_order_payments");

            migrationBuilder.DropForeignKey(
                name: "FK_custom_orders_cafe_customers_CafeCustomerId",
                table: "custom_orders");

            migrationBuilder.DropTable(
                name: "cafe_prices");

            migrationBuilder.DropTable(
                name: "custom_order_lines");

            migrationBuilder.DropTable(
                name: "cafe_customers");

            migrationBuilder.DropIndex(
                name: "IX_custom_orders_CafeCustomerId",
                table: "custom_orders");

            migrationBuilder.DropIndex(
                name: "IX_custom_order_payments_CafeCustomerId_PaidAtUtc",
                table: "custom_order_payments");

            migrationBuilder.DropColumn(
                name: "CafeCustomerId",
                table: "custom_orders");

            migrationBuilder.DropColumn(
                name: "CafeCustomerId",
                table: "custom_order_payments");
        }
    }
}
