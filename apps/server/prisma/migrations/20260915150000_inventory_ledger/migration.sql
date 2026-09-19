CREATE TABLE "inventory_transactions" (
  "id" UUID PRIMARY KEY,
  "site_id" UUID NOT NULL REFERENCES "sites"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  "user_id" UUID NOT NULL REFERENCES "users"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  "reference_id" UUID NOT NULL,
  "kind" VARCHAR(40) NOT NULL CHECK ("kind" IN ('IncomingReceipt','StockToDisplay','RetailSale','CafeIssue','KitchenReturn','DisplayReturnToStock','ApprovedAdjustment')),
  "reason" VARCHAR(500) NOT NULL,
  "occurred_at" TIMESTAMPTZ(6) NOT NULL,
  "source_event_id" UUID NOT NULL
);
CREATE UNIQUE INDEX "inventory_transactions_source_event_id_key" ON "inventory_transactions"("source_event_id");
CREATE UNIQUE INDEX "inventory_transactions_site_id_kind_reference_id_key" ON "inventory_transactions"("site_id","kind","reference_id");
CREATE INDEX "inventory_transactions_site_id_occurred_at_idx" ON "inventory_transactions"("site_id","occurred_at");
CREATE TABLE "inventory_transaction_lines" (
  "id" UUID PRIMARY KEY,
  "transaction_id" UUID NOT NULL REFERENCES "inventory_transactions"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  "item_id" UUID NOT NULL REFERENCES "items"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  "location" "StockLocation" NOT NULL,
  "delta_scaled" BIGINT NOT NULL CHECK ("delta_scaled" <> 0)
);
CREATE UNIQUE INDEX "inventory_transaction_lines_transaction_id_item_id_location_key" ON "inventory_transaction_lines"("transaction_id","item_id","location");
CREATE INDEX "inventory_transaction_lines_item_id_location_idx" ON "inventory_transaction_lines"("item_id","location");
ALTER TABLE "stock_balances" ADD CONSTRAINT "stock_balances_quantity_nonnegative" CHECK ("quantity_scaled" >= 0);
