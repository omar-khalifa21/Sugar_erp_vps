-- CreateTable
CREATE TABLE "public"."kitchen_requests" (
    "id" UUID NOT NULL,
    "requesting_site_id" UUID NOT NULL,
    "kitchen_site_id" UUID NOT NULL,
    "status" VARCHAR(30) NOT NULL DEFAULT 'REQUESTED',
    "version" INTEGER NOT NULL DEFAULT 1,
    "business_date" VARCHAR(10),
    "submitted_at" TIMESTAMPTZ(6) NOT NULL,
    "source_event_id" UUID NOT NULL,

    CONSTRAINT "kitchen_requests_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "public"."kitchen_request_lines" (
    "id" UUID NOT NULL,
    "request_id" UUID NOT NULL,
    "item_id" UUID NOT NULL,
    "requested_scaled" BIGINT NOT NULL,
    "sent_scaled" BIGINT NOT NULL DEFAULT 0,
    "quantity_scale" INTEGER NOT NULL,
    "name_snapshot" VARCHAR(300) NOT NULL,
    "unit_snapshot" VARCHAR(30) NOT NULL,

    CONSTRAINT "kitchen_request_lines_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "public"."kitchen_shipments" (
    "id" UUID NOT NULL,
    "request_id" UUID NOT NULL,
    "source_kitchen_site_id" UUID NOT NULL,
    "destination_site_id" UUID NOT NULL,
    "reference" VARCHAR(120) NOT NULL,
    "status" VARCHAR(30) NOT NULL DEFAULT 'SENT',
    "version" INTEGER NOT NULL DEFAULT 1,
    "dispatched_at" TIMESTAMPTZ(6) NOT NULL,
    "source_event_id" UUID NOT NULL,

    CONSTRAINT "kitchen_shipments_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "public"."kitchen_shipment_lines" (
    "id" UUID NOT NULL,
    "shipment_id" UUID NOT NULL,
    "request_line_id" UUID NOT NULL,
    "item_id" UUID NOT NULL,
    "sent_scaled" BIGINT NOT NULL,

    CONSTRAINT "kitchen_shipment_lines_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "public"."kitchen_incoming_receipts" (
    "id" UUID NOT NULL,
    "shipment_id" UUID NOT NULL,
    "status" VARCHAR(30) NOT NULL,
    "counted_at" TIMESTAMPTZ(6) NOT NULL,
    "source_event_id" UUID NOT NULL,

    CONSTRAINT "kitchen_incoming_receipts_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "public"."kitchen_incoming_receipt_lines" (
    "id" UUID NOT NULL,
    "receipt_id" UUID NOT NULL,
    "shipment_line_id" UUID NOT NULL,
    "counted_scaled" BIGINT NOT NULL,
    "confirmed_scaled" BIGINT,

    CONSTRAINT "kitchen_incoming_receipt_lines_pkey" PRIMARY KEY ("id")
);

ALTER TABLE "public"."kitchen_requests" ADD CONSTRAINT "kitchen_requests_status_check" CHECK ("status" IN ('REQUESTED','PARTIAL','FULFILLED','REJECTED','CANCELLED') AND "version" > 0);
ALTER TABLE "public"."kitchen_request_lines" ADD CONSTRAINT "kitchen_request_lines_quantity_check" CHECK ("requested_scaled" > 0 AND "sent_scaled" >= 0 AND "sent_scaled" <= "requested_scaled" AND "quantity_scale" > 0);
ALTER TABLE "public"."kitchen_shipments" ADD CONSTRAINT "kitchen_shipments_status_check" CHECK ("status" IN ('SENT','RECEIVED','CONFLICT','ADMIN_RESOLVED') AND "version" > 0);
ALTER TABLE "public"."kitchen_shipment_lines" ADD CONSTRAINT "kitchen_shipment_lines_quantity_check" CHECK ("sent_scaled" > 0);
ALTER TABLE "public"."kitchen_incoming_receipts" ADD CONSTRAINT "kitchen_incoming_receipts_status_check" CHECK ("status" IN ('ACCEPTED','DISPUTED','ADMIN_RESOLVED'));
ALTER TABLE "public"."kitchen_incoming_receipt_lines" ADD CONSTRAINT "kitchen_incoming_receipt_lines_quantity_check" CHECK ("counted_scaled" >= 0 AND ("confirmed_scaled" IS NULL OR "confirmed_scaled" >= 0));

-- CreateIndex
CREATE UNIQUE INDEX "kitchen_requests_source_event_id_key" ON "public"."kitchen_requests"("source_event_id");

-- CreateIndex
CREATE INDEX "kitchen_requests_kitchen_site_id_status_submitted_at_idx" ON "public"."kitchen_requests"("kitchen_site_id", "status", "submitted_at");

-- CreateIndex
CREATE INDEX "kitchen_requests_requesting_site_id_submitted_at_idx" ON "public"."kitchen_requests"("requesting_site_id", "submitted_at");

-- CreateIndex
CREATE INDEX "kitchen_request_lines_request_id_idx" ON "public"."kitchen_request_lines"("request_id");

-- CreateIndex
CREATE UNIQUE INDEX "kitchen_request_lines_request_id_item_id_key" ON "public"."kitchen_request_lines"("request_id", "item_id");

-- CreateIndex
CREATE UNIQUE INDEX "kitchen_shipments_source_event_id_key" ON "public"."kitchen_shipments"("source_event_id");

-- CreateIndex
CREATE INDEX "kitchen_shipments_destination_site_id_status_dispatched_at_idx" ON "public"."kitchen_shipments"("destination_site_id", "status", "dispatched_at");

-- CreateIndex
CREATE INDEX "kitchen_shipments_request_id_dispatched_at_idx" ON "public"."kitchen_shipments"("request_id", "dispatched_at");

-- CreateIndex
CREATE INDEX "kitchen_shipment_lines_shipment_id_idx" ON "public"."kitchen_shipment_lines"("shipment_id");

-- CreateIndex
CREATE UNIQUE INDEX "kitchen_shipment_lines_shipment_id_request_line_id_key" ON "public"."kitchen_shipment_lines"("shipment_id", "request_line_id");

-- CreateIndex
CREATE UNIQUE INDEX "kitchen_incoming_receipts_shipment_id_key" ON "public"."kitchen_incoming_receipts"("shipment_id");

-- CreateIndex
CREATE UNIQUE INDEX "kitchen_incoming_receipts_source_event_id_key" ON "public"."kitchen_incoming_receipts"("source_event_id");

-- CreateIndex
CREATE UNIQUE INDEX "kitchen_incoming_receipt_lines_shipment_line_id_key" ON "public"."kitchen_incoming_receipt_lines"("shipment_line_id");

-- CreateIndex
CREATE INDEX "kitchen_incoming_receipt_lines_receipt_id_idx" ON "public"."kitchen_incoming_receipt_lines"("receipt_id");

-- AddForeignKey
ALTER TABLE "public"."kitchen_requests" ADD CONSTRAINT "kitchen_requests_requesting_site_id_fkey" FOREIGN KEY ("requesting_site_id") REFERENCES "public"."sites"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "public"."kitchen_requests" ADD CONSTRAINT "kitchen_requests_kitchen_site_id_fkey" FOREIGN KEY ("kitchen_site_id") REFERENCES "public"."sites"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "public"."kitchen_request_lines" ADD CONSTRAINT "kitchen_request_lines_request_id_fkey" FOREIGN KEY ("request_id") REFERENCES "public"."kitchen_requests"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "public"."kitchen_shipments" ADD CONSTRAINT "kitchen_shipments_request_id_fkey" FOREIGN KEY ("request_id") REFERENCES "public"."kitchen_requests"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "public"."kitchen_shipments" ADD CONSTRAINT "kitchen_shipments_source_kitchen_site_id_fkey" FOREIGN KEY ("source_kitchen_site_id") REFERENCES "public"."sites"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "public"."kitchen_shipments" ADD CONSTRAINT "kitchen_shipments_destination_site_id_fkey" FOREIGN KEY ("destination_site_id") REFERENCES "public"."sites"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "public"."kitchen_shipment_lines" ADD CONSTRAINT "kitchen_shipment_lines_shipment_id_fkey" FOREIGN KEY ("shipment_id") REFERENCES "public"."kitchen_shipments"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "public"."kitchen_shipment_lines" ADD CONSTRAINT "kitchen_shipment_lines_request_line_id_fkey" FOREIGN KEY ("request_line_id") REFERENCES "public"."kitchen_request_lines"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "public"."kitchen_incoming_receipts" ADD CONSTRAINT "kitchen_incoming_receipts_shipment_id_fkey" FOREIGN KEY ("shipment_id") REFERENCES "public"."kitchen_shipments"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "public"."kitchen_incoming_receipt_lines" ADD CONSTRAINT "kitchen_incoming_receipt_lines_receipt_id_fkey" FOREIGN KEY ("receipt_id") REFERENCES "public"."kitchen_incoming_receipts"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "public"."kitchen_incoming_receipt_lines" ADD CONSTRAINT "kitchen_incoming_receipt_lines_shipment_line_id_fkey" FOREIGN KEY ("shipment_line_id") REFERENCES "public"."kitchen_shipment_lines"("id") ON DELETE RESTRICT ON UPDATE CASCADE;
