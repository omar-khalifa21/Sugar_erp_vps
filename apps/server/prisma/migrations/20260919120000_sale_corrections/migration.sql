CREATE TABLE "sale_corrections" (
    "id" UUID NOT NULL,
    "original_sale_id" UUID NOT NULL,
    "refund_minor" BIGINT NOT NULL,
    "reason" VARCHAR(500) NOT NULL,
    "actor" VARCHAR(200) NOT NULL,
    "refund_method" VARCHAR(20) NOT NULL,
    "lines" JSONB NOT NULL,
    "occurred_at" TIMESTAMPTZ(6) NOT NULL,
    "source_event_id" UUID NOT NULL,
    CONSTRAINT "sale_corrections_pkey" PRIMARY KEY ("id"),
    CONSTRAINT "sale_corrections_refund_positive" CHECK ("refund_minor" > 0),
    CONSTRAINT "sale_corrections_original_sale_id_fkey" FOREIGN KEY ("original_sale_id") REFERENCES "retail_sales"("id") ON DELETE RESTRICT ON UPDATE CASCADE
);

CREATE UNIQUE INDEX "sale_corrections_source_event_id_key" ON "sale_corrections"("source_event_id");
CREATE INDEX "sale_corrections_original_sale_id_occurred_at_idx" ON "sale_corrections"("original_sale_id", "occurred_at");
