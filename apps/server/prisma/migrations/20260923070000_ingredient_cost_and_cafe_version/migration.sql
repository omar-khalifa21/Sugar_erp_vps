ALTER TABLE "stock_balances" ADD COLUMN "inventory_cost_minor" BIGINT NOT NULL DEFAULT 0;
ALTER TABLE "cafe_customers" ADD COLUMN "version" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "cafe_invoices" ADD COLUMN "fulfilled_at" TIMESTAMPTZ(6),
  ADD COLUMN "fulfillment_event_id" UUID,
  ADD COLUMN "fulfillment_cost_minor" BIGINT;
CREATE UNIQUE INDEX "cafe_invoices_fulfillment_event_id_key" ON "cafe_invoices"("fulfillment_event_id");

CREATE TABLE "ingredient_stock_transactions" (
  "id" UUID NOT NULL,
  "site_id" UUID NOT NULL,
  "item_id" UUID NOT NULL,
  "source_event_id" UUID NOT NULL,
  "kind" VARCHAR(40) NOT NULL,
  "quantity_delta_scaled" BIGINT NOT NULL,
  "cost_minor" BIGINT NOT NULL DEFAULT 0,
  "reason" VARCHAR(500) NOT NULL DEFAULT '',
  "occurred_at" TIMESTAMPTZ(6) NOT NULL,
  CONSTRAINT "ingredient_stock_transactions_pkey" PRIMARY KEY ("id"),
  CONSTRAINT "ingredient_stock_transactions_site_id_fkey" FOREIGN KEY ("site_id") REFERENCES "sites"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "ingredient_stock_transactions_item_id_fkey" FOREIGN KEY ("item_id") REFERENCES "items"("id") ON DELETE RESTRICT ON UPDATE CASCADE
);
CREATE UNIQUE INDEX "ingredient_stock_transactions_source_event_id_item_id_kind_key" ON "ingredient_stock_transactions"("source_event_id", "item_id", "kind");
CREATE INDEX "ingredient_stock_transactions_site_id_item_id_occurred_at_idx" ON "ingredient_stock_transactions"("site_id", "item_id", "occurred_at");
