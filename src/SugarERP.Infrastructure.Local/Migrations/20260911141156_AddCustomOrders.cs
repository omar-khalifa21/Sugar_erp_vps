using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SugarERP.Infrastructure.Local.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_sequence_positive",
                table: "sequence_state");

            migrationBuilder.AddColumn<int>(
                name: "NextCustomOrderSequence",
                table: "sequence_state",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "custom_orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommandId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrderNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CustomerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CustomerPhone = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    DueAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TotalMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_orders", x => x.Id);
                    table.CheckConstraint("ck_custom_order_money", "\"TotalMinor\" >= 0");
                    table.CheckConstraint("ck_custom_order_version", "\"Version\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "custom_order_activities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommandId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_order_activities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_custom_order_activities_custom_orders_CustomOrderId",
                        column: x => x.CustomOrderId,
                        principalTable: "custom_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "custom_order_payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommandId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShiftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Method = table.Column<string>(type: "TEXT", nullable: false),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    PaidAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_order_payments", x => x.Id);
                    table.CheckConstraint("ck_custom_order_payment_amount", "\"AmountMinor\" > 0");
                    table.ForeignKey(
                        name: "FK_custom_order_payments_custom_orders_CustomOrderId",
                        column: x => x.CustomOrderId,
                        principalTable: "custom_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_sequence_positive",
                table: "sequence_state",
                sql: "\"NextDeviceSequence\" > 0 AND \"NextReceiptSequence\" > 0 AND \"NextCustomOrderSequence\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_custom_order_activities_CommandId",
                table: "custom_order_activities",
                column: "CommandId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_custom_order_activities_CustomOrderId_OccurredAtUtc",
                table: "custom_order_activities",
                columns: new[] { "CustomOrderId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_custom_order_payments_CommandId",
                table: "custom_order_payments",
                column: "CommandId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_custom_order_payments_CustomOrderId_PaidAtUtc",
                table: "custom_order_payments",
                columns: new[] { "CustomOrderId", "PaidAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_custom_orders_CommandId",
                table: "custom_orders",
                column: "CommandId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_custom_orders_OrderNumber",
                table: "custom_orders",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_custom_orders_Status_DueAtUtc",
                table: "custom_orders",
                columns: new[] { "Status", "DueAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "custom_order_activities");

            migrationBuilder.DropTable(
                name: "custom_order_payments");

            migrationBuilder.DropTable(
                name: "custom_orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sequence_positive",
                table: "sequence_state");

            migrationBuilder.DropColumn(
                name: "NextCustomOrderSequence",
                table: "sequence_state");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sequence_positive",
                table: "sequence_state",
                sql: "\"NextDeviceSequence\" > 0 AND \"NextReceiptSequence\" > 0");
        }
    }
}
