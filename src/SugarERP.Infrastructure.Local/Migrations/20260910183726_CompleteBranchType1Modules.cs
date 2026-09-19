using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SugarERP.Infrastructure.Local.Migrations
{
    /// <inheritdoc />
    public partial class CompleteBranchType1Modules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CustomerRestockScaled",
                table: "shift_item_snapshots",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "DiscrepancyScaled",
                table: "shift_item_snapshots",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "adjustment_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CountLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProposedDeltaScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    AdminDecisionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AppliedDocumentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_adjustment_requests", x => x.Id);
                    table.CheckConstraint("ck_adjustment_request_nonzero", "\"ProposedDeltaScaled\" <> 0");
                });

            migrationBuilder.CreateTable(
                name: "branch_local_settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ExportDirectory = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    PrinterName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branch_local_settings", x => x.Id);
                    table.CheckConstraint("ck_branch_settings_singleton", "\"Id\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "kitchen_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommandId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    BusinessDate = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kitchen_requests", x => x.Id);
                    table.CheckConstraint("ck_kitchen_request_version", "\"Version\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "kitchen_returns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommandId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShiftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    DispatchedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kitchen_returns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "quantity_conflicts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ReportedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quantity_conflicts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "remote_stock_projections",
                columns: table => new
                {
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SiteName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ItemName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    QuantityScale = table.Column<int>(type: "INTEGER", nullable: false),
                    QuantityScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    AsOfUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_remote_stock_projections", x => new { x.SiteId, x.ItemId });
                    table.CheckConstraint("ck_remote_stock_scale", "\"QuantityScale\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "report_artifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShiftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReportVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Format = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    LocalPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ByteLength = table.Column<long>(type: "INTEGER", nullable: false),
                    GeneratedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_artifacts", x => x.Id);
                    table.CheckConstraint("ck_report_artifact", "\"ReportVersion\" > 0 AND \"Format\" = 'xlsx' AND \"ByteLength\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "sale_corrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommandId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginalSaleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PostingShiftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RefundTotalMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    RefundMethod = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_corrections", x => x.Id);
                    table.CheckConstraint("ck_sale_correction_refund", "\"RefundTotalMinor\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "shipments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommandId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    DispatchedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SyntheticDemo = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipments", x => x.Id);
                    table.CheckConstraint("ck_shipment_version", "\"Version\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "side_effect_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    DocumentVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_side_effect_jobs", x => x.Id);
                    table.CheckConstraint("ck_side_effect_job_version", "\"DocumentVersion\" > 0 AND \"Attempts\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "stock_counts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommandId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShiftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CountedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_counts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stock_holds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReceiptLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PhysicalCountScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    ReleasedScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    DecisionId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_holds", x => x.Id);
                    table.CheckConstraint("ck_stock_hold_quantity", "\"PhysicalCountScaled\" >= 0 AND \"ReleasedScaled\" >= 0 AND \"ReleasedScaled\" <= \"PhysicalCountScaled\"");
                });

            migrationBuilder.CreateTable(
                name: "sync_cursors",
                columns: table => new
                {
                    FeedScope = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Cursor = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    SnapshotVersion = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_cursors", x => x.FeedScope);
                });

            migrationBuilder.CreateTable(
                name: "kitchen_request_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NameSnapshot = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    UnitSnapshot = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    QuantityScale = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestedScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    ApprovedScaled = table.Column<long>(type: "INTEGER", nullable: true),
                    SentScaled = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kitchen_request_lines", x => x.Id);
                    table.CheckConstraint("ck_kitchen_request_line_quantity", "\"RequestedScaled\" > 0 AND \"QuantityScale\" > 0 AND \"SentScaled\" >= 0");
                    table.ForeignKey(
                        name: "FK_kitchen_request_lines_kitchen_requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "kitchen_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "kitchen_return_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReturnId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NameSnapshot = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    UnitSnapshot = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    QuantityScale = table.Column<int>(type: "INTEGER", nullable: false),
                    SentScaled = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kitchen_return_lines", x => x.Id);
                    table.CheckConstraint("ck_kitchen_return_line_quantity", "\"SentScaled\" > 0 AND \"QuantityScale\" > 0");
                    table.ForeignKey(
                        name: "FK_kitchen_return_lines_kitchen_returns_ReturnId",
                        column: x => x.ReturnId,
                        principalTable: "kitchen_returns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "conflict_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConflictId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReceiptLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SentScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    CountedScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conflict_lines", x => x.Id);
                    table.CheckConstraint("ck_conflict_line_quantity", "\"SentScaled\" >= 0 AND \"CountedScaled\" >= 0");
                    table.ForeignKey(
                        name: "FK_conflict_lines_quantity_conflicts_ConflictId",
                        column: x => x.ConflictId,
                        principalTable: "quantity_conflicts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "correction_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CorrectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginalSaleLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuantityDeltaScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    AmountDeltaMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    RestockScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    Disposition = table.Column<string>(type: "TEXT", nullable: false),
                    NameSnapshot = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    UnitSnapshot = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    QuantityScale = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_correction_lines", x => x.Id);
                    table.CheckConstraint("ck_correction_line_quantity", "\"QuantityDeltaScaled\" < 0 AND \"AmountDeltaMinor\" < 0 AND \"RestockScaled\" >= 0 AND \"QuantityScale\" > 0");
                    table.ForeignKey(
                        name: "FK_correction_lines_sale_corrections_CorrectionId",
                        column: x => x.CorrectionId,
                        principalTable: "sale_corrections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refund_payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CorrectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Method = table.Column<string>(type: "TEXT", nullable: false),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    PaidAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refund_payments", x => x.Id);
                    table.CheckConstraint("ck_refund_payment_amount", "\"AmountMinor\" > 0");
                    table.ForeignKey(
                        name: "FK_refund_payments_sale_corrections_CorrectionId",
                        column: x => x.CorrectionId,
                        principalTable: "sale_corrections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "incoming_receipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommandId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReceivingShiftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CountedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    AcceptedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incoming_receipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_incoming_receipts_shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shipment_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestLineId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NameSnapshot = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    UnitSnapshot = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    QuantityScale = table.Column<int>(type: "INTEGER", nullable: false),
                    SentScaled = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipment_lines", x => x.Id);
                    table.CheckConstraint("ck_shipment_line_quantity", "\"SentScaled\" > 0 AND \"QuantityScale\" > 0");
                    table.ForeignKey(
                        name: "FK_shipment_lines_shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_count_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NameSnapshot = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    UnitSnapshot = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    QuantityScale = table.Column<int>(type: "INTEGER", nullable: false),
                    ExpectedScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    ActualScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    DifferenceScaled = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_count_lines", x => x.Id);
                    table.CheckConstraint("ck_stock_count_line_quantity", "\"ExpectedScaled\" >= 0 AND \"ActualScaled\" >= 0 AND \"QuantityScale\" > 0 AND \"DifferenceScaled\" = \"ActualScaled\" - \"ExpectedScaled\"");
                    table.ForeignKey(
                        name: "FK_stock_count_lines_stock_counts_CountId",
                        column: x => x.CountId,
                        principalTable: "stock_counts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "incoming_receipt_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShipmentLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SentScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    CountedScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    ConfirmedScaled = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incoming_receipt_lines", x => x.Id);
                    table.CheckConstraint("ck_receipt_line_quantity", "\"SentScaled\" > 0 AND \"CountedScaled\" >= 0 AND (\"ConfirmedScaled\" IS NULL OR \"ConfirmedScaled\" >= 0)");
                    table.ForeignKey(
                        name: "FK_incoming_receipt_lines_incoming_receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "incoming_receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_adjustment_requests_CountLineId",
                table: "adjustment_requests",
                column: "CountLineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_conflict_lines_ConflictId",
                table: "conflict_lines",
                column: "ConflictId");

            migrationBuilder.CreateIndex(
                name: "IX_conflict_lines_ReceiptLineId",
                table: "conflict_lines",
                column: "ReceiptLineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_correction_lines_CorrectionId_OriginalSaleLineId",
                table: "correction_lines",
                columns: new[] { "CorrectionId", "OriginalSaleLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_incoming_receipt_lines_ReceiptId_ItemId",
                table: "incoming_receipt_lines",
                columns: new[] { "ReceiptId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_incoming_receipt_lines_ShipmentLineId",
                table: "incoming_receipt_lines",
                column: "ShipmentLineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_incoming_receipts_CommandId",
                table: "incoming_receipts",
                column: "CommandId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_incoming_receipts_ShipmentId",
                table: "incoming_receipts",
                column: "ShipmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_kitchen_request_lines_RequestId_ItemId",
                table: "kitchen_request_lines",
                columns: new[] { "RequestId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_kitchen_requests_BusinessDate_RequestedAtUtc",
                table: "kitchen_requests",
                columns: new[] { "BusinessDate", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_kitchen_requests_CommandId",
                table: "kitchen_requests",
                column: "CommandId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_kitchen_return_lines_ReturnId_ItemId",
                table: "kitchen_return_lines",
                columns: new[] { "ReturnId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_kitchen_returns_CommandId",
                table: "kitchen_returns",
                column: "CommandId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_kitchen_returns_Reference",
                table: "kitchen_returns",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_kitchen_returns_ShiftId_DispatchedAtUtc",
                table: "kitchen_returns",
                columns: new[] { "ShiftId", "DispatchedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_quantity_conflicts_ReceiptId",
                table: "quantity_conflicts",
                column: "ReceiptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refund_payments_CorrectionId",
                table: "refund_payments",
                column: "CorrectionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_report_artifacts_ShiftId_ReportVersion",
                table: "report_artifacts",
                columns: new[] { "ShiftId", "ReportVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sale_corrections_CommandId",
                table: "sale_corrections",
                column: "CommandId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sale_corrections_OriginalSaleId_OccurredAtUtc",
                table: "sale_corrections",
                columns: new[] { "OriginalSaleId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_shipment_lines_RequestLineId",
                table: "shipment_lines",
                column: "RequestLineId");

            migrationBuilder.CreateIndex(
                name: "IX_shipment_lines_ShipmentId_ItemId",
                table: "shipment_lines",
                columns: new[] { "ShipmentId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shipments_CommandId",
                table: "shipments",
                column: "CommandId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shipments_Reference",
                table: "shipments",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shipments_Status_DispatchedAtUtc",
                table: "shipments",
                columns: new[] { "Status", "DispatchedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_side_effect_jobs_SourceId_Kind_DocumentVersion",
                table: "side_effect_jobs",
                columns: new[] { "SourceId", "Kind", "DocumentVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_side_effect_jobs_State_NextAttemptAtUtc",
                table: "side_effect_jobs",
                columns: new[] { "State", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_count_lines_CountId_ItemId",
                table: "stock_count_lines",
                columns: new[] { "CountId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_counts_CommandId",
                table: "stock_counts",
                column: "CommandId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_counts_ShiftId",
                table: "stock_counts",
                column: "ShiftId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_holds_ReceiptLineId",
                table: "stock_holds",
                column: "ReceiptLineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_holds_Status_ItemId",
                table: "stock_holds",
                columns: new[] { "Status", "ItemId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "adjustment_requests");

            migrationBuilder.DropTable(
                name: "branch_local_settings");

            migrationBuilder.DropTable(
                name: "conflict_lines");

            migrationBuilder.DropTable(
                name: "correction_lines");

            migrationBuilder.DropTable(
                name: "incoming_receipt_lines");

            migrationBuilder.DropTable(
                name: "kitchen_request_lines");

            migrationBuilder.DropTable(
                name: "kitchen_return_lines");

            migrationBuilder.DropTable(
                name: "refund_payments");

            migrationBuilder.DropTable(
                name: "remote_stock_projections");

            migrationBuilder.DropTable(
                name: "report_artifacts");

            migrationBuilder.DropTable(
                name: "shipment_lines");

            migrationBuilder.DropTable(
                name: "side_effect_jobs");

            migrationBuilder.DropTable(
                name: "stock_count_lines");

            migrationBuilder.DropTable(
                name: "stock_holds");

            migrationBuilder.DropTable(
                name: "sync_cursors");

            migrationBuilder.DropTable(
                name: "quantity_conflicts");

            migrationBuilder.DropTable(
                name: "incoming_receipts");

            migrationBuilder.DropTable(
                name: "kitchen_requests");

            migrationBuilder.DropTable(
                name: "kitchen_returns");

            migrationBuilder.DropTable(
                name: "sale_corrections");

            migrationBuilder.DropTable(
                name: "stock_counts");

            migrationBuilder.DropTable(
                name: "shipments");

            migrationBuilder.DropColumn(
                name: "CustomerRestockScaled",
                table: "shift_item_snapshots");

            migrationBuilder.DropColumn(
                name: "DiscrepancyScaled",
                table: "shift_item_snapshots");
        }
    }
}
