CREATE TYPE "StockLocation" AS ENUM ('SALEABLE', 'FREEZER', 'DISPLAY', 'KITCHEN', 'HOLD', 'TRANSIT');
CREATE TYPE "ShiftKind" AS ENUM ('MORNING', 'EVENING');
CREATE TYPE "AdjustmentStatus" AS ENUM ('PENDING_SITE_APPLY', 'APPLIED', 'REJECTED');

CREATE TABLE "stock_balances" (
  "id" UUID NOT NULL,
  "site_id" UUID NOT NULL,
  "item_id" UUID NOT NULL,
  "location" "StockLocation" NOT NULL,
  "quantity_scaled" BIGINT NOT NULL,
  "as_of_at" TIMESTAMPTZ(6) NOT NULL,
  "version" INTEGER NOT NULL DEFAULT 1,
  "source_event_id" UUID,
  CONSTRAINT "stock_balances_pkey" PRIMARY KEY ("id"),
  CONSTRAINT "stock_balances_site_id_fkey" FOREIGN KEY ("site_id") REFERENCES "sites"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "stock_balances_item_id_fkey" FOREIGN KEY ("item_id") REFERENCES "items"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "stock_balances_version_check" CHECK ("version" > 0)
);
CREATE UNIQUE INDEX "stock_balances_site_id_item_id_location_key" ON "stock_balances"("site_id", "item_id", "location");
CREATE INDEX "stock_balances_site_id_as_of_at_idx" ON "stock_balances"("site_id", "as_of_at");

CREATE TABLE "retail_sales" (
  "id" UUID NOT NULL,
  "site_id" UUID NOT NULL,
  "receipt_number" VARCHAR(100) NOT NULL,
  "business_date" DATE NOT NULL,
  "shift_kind" "ShiftKind" NOT NULL,
  "net_minor" BIGINT NOT NULL,
  "tip_minor" BIGINT NOT NULL DEFAULT 0,
  "occurred_at" TIMESTAMPTZ(6) NOT NULL,
  "source_event_id" UUID,
  "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT "retail_sales_pkey" PRIMARY KEY ("id"),
  CONSTRAINT "retail_sales_site_id_fkey" FOREIGN KEY ("site_id") REFERENCES "sites"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "retail_sales_net_minor_check" CHECK ("net_minor" >= 0),
  CONSTRAINT "retail_sales_tip_minor_check" CHECK ("tip_minor" >= 0)
);
CREATE UNIQUE INDEX "retail_sales_source_event_id_key" ON "retail_sales"("source_event_id");
CREATE UNIQUE INDEX "retail_sales_site_id_receipt_number_key" ON "retail_sales"("site_id", "receipt_number");
CREATE INDEX "retail_sales_site_id_business_date_idx" ON "retail_sales"("site_id", "business_date");

CREATE TABLE "stock_adjustments" (
  "id" UUID NOT NULL,
  "site_id" UUID NOT NULL,
  "item_id" UUID NOT NULL,
  "location" "StockLocation" NOT NULL,
  "delta_scaled" BIGINT NOT NULL,
  "reason" VARCHAR(500) NOT NULL,
  "expected_version" INTEGER NOT NULL,
  "status" "AdjustmentStatus" NOT NULL DEFAULT 'PENDING_SITE_APPLY',
  "created_by_user_id" UUID NOT NULL,
  "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
  "applied_at" TIMESTAMPTZ(6),
  CONSTRAINT "stock_adjustments_pkey" PRIMARY KEY ("id"),
  CONSTRAINT "stock_adjustments_site_id_fkey" FOREIGN KEY ("site_id") REFERENCES "sites"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "stock_adjustments_item_id_fkey" FOREIGN KEY ("item_id") REFERENCES "items"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "stock_adjustments_created_by_user_id_fkey" FOREIGN KEY ("created_by_user_id") REFERENCES "users"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "stock_adjustments_nonzero_check" CHECK ("delta_scaled" <> 0),
  CONSTRAINT "stock_adjustments_expected_version_check" CHECK ("expected_version" > 0)
);
CREATE INDEX "stock_adjustments_site_id_status_created_at_idx" ON "stock_adjustments"("site_id", "status", "created_at");
