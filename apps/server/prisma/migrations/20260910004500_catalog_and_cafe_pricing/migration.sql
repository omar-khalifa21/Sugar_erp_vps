ALTER TABLE "items"
ADD COLUMN "retail_price_minor" INTEGER NOT NULL DEFAULT 0,
ADD CONSTRAINT "items_retail_price_minor_check" CHECK ("retail_price_minor" >= 0);

ALTER TABLE "cafe_customers"
ADD COLUMN "contact" VARCHAR(200),
ADD COLUMN "notes" VARCHAR(500);

CREATE TABLE "retail_price_revisions" (
  "id" UUID NOT NULL,
  "item_id" UUID NOT NULL,
  "price_minor" INTEGER NOT NULL,
  "version" INTEGER NOT NULL,
  "effective_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT "retail_price_revisions_pkey" PRIMARY KEY ("id"),
  CONSTRAINT "retail_price_revisions_item_id_fkey" FOREIGN KEY ("item_id") REFERENCES "items"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "retail_price_revisions_price_minor_check" CHECK ("price_minor" >= 0),
  CONSTRAINT "retail_price_revisions_version_check" CHECK ("version" > 0)
);
CREATE UNIQUE INDEX "retail_price_revisions_item_id_version_key" ON "retail_price_revisions"("item_id", "version");
CREATE INDEX "retail_price_revisions_item_id_effective_at_idx" ON "retail_price_revisions"("item_id", "effective_at");

CREATE TABLE "cafe_item_prices" (
  "id" UUID NOT NULL,
  "customer_id" UUID NOT NULL,
  "item_id" UUID NOT NULL,
  "price_minor" INTEGER NOT NULL,
  "version" INTEGER NOT NULL DEFAULT 1,
  "updated_at" TIMESTAMPTZ(6) NOT NULL,
  CONSTRAINT "cafe_item_prices_pkey" PRIMARY KEY ("id"),
  CONSTRAINT "cafe_item_prices_customer_id_fkey" FOREIGN KEY ("customer_id") REFERENCES "cafe_customers"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "cafe_item_prices_item_id_fkey" FOREIGN KEY ("item_id") REFERENCES "items"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "cafe_item_prices_price_minor_check" CHECK ("price_minor" >= 0),
  CONSTRAINT "cafe_item_prices_version_check" CHECK ("version" > 0)
);
CREATE UNIQUE INDEX "cafe_item_prices_customer_id_item_id_key" ON "cafe_item_prices"("customer_id", "item_id");
CREATE INDEX "cafe_item_prices_item_id_idx" ON "cafe_item_prices"("item_id");

CREATE TABLE "cafe_price_revisions" (
  "id" UUID NOT NULL,
  "cafe_price_id" UUID NOT NULL,
  "customer_id" UUID NOT NULL,
  "item_id" UUID NOT NULL,
  "price_minor" INTEGER NOT NULL,
  "version" INTEGER NOT NULL,
  "effective_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT "cafe_price_revisions_pkey" PRIMARY KEY ("id"),
  CONSTRAINT "cafe_price_revisions_cafe_price_id_fkey" FOREIGN KEY ("cafe_price_id") REFERENCES "cafe_item_prices"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "cafe_price_revisions_customer_id_fkey" FOREIGN KEY ("customer_id") REFERENCES "cafe_customers"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "cafe_price_revisions_item_id_fkey" FOREIGN KEY ("item_id") REFERENCES "items"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "cafe_price_revisions_price_minor_check" CHECK ("price_minor" >= 0),
  CONSTRAINT "cafe_price_revisions_version_check" CHECK ("version" > 0)
);
CREATE UNIQUE INDEX "cafe_price_revisions_cafe_price_id_version_key" ON "cafe_price_revisions"("cafe_price_id", "version");
CREATE INDEX "cafe_price_revisions_customer_id_item_id_effective_at_idx" ON "cafe_price_revisions"("customer_id", "item_id", "effective_at");

CREATE TABLE "cafe_invoice_lines" (
  "id" UUID NOT NULL,
  "invoice_id" UUID NOT NULL,
  "item_id" UUID NOT NULL,
  "quantity_scaled" BIGINT NOT NULL,
  "quantity_scale" INTEGER NOT NULL,
  "unit_price_minor" INTEGER NOT NULL,
  "total_minor" BIGINT NOT NULL,
  "item_name_snapshot" VARCHAR(300) NOT NULL,
  "sku_snapshot" VARCHAR(100) NOT NULL,
  "unit_snapshot" VARCHAR(30) NOT NULL,
  CONSTRAINT "cafe_invoice_lines_pkey" PRIMARY KEY ("id"),
  CONSTRAINT "cafe_invoice_lines_invoice_id_fkey" FOREIGN KEY ("invoice_id") REFERENCES "cafe_invoices"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "cafe_invoice_lines_item_id_fkey" FOREIGN KEY ("item_id") REFERENCES "items"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "cafe_invoice_lines_quantity_scaled_check" CHECK ("quantity_scaled" > 0),
  CONSTRAINT "cafe_invoice_lines_quantity_scale_check" CHECK ("quantity_scale" > 0),
  CONSTRAINT "cafe_invoice_lines_unit_price_minor_check" CHECK ("unit_price_minor" >= 0),
  CONSTRAINT "cafe_invoice_lines_total_minor_check" CHECK ("total_minor" >= 0)
);
CREATE UNIQUE INDEX "cafe_invoice_lines_invoice_id_item_id_key" ON "cafe_invoice_lines"("invoice_id", "item_id");
CREATE INDEX "cafe_invoice_lines_item_id_idx" ON "cafe_invoice_lines"("item_id");
