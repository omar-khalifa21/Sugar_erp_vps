CREATE TABLE "site_retail_prices" (
  "id" UUID NOT NULL,
  "site_id" UUID NOT NULL,
  "item_id" UUID NOT NULL,
  "price_minor" INTEGER NOT NULL,
  "version" INTEGER NOT NULL DEFAULT 1,
  "updated_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT "site_retail_prices_pkey" PRIMARY KEY ("id"),
  CONSTRAINT "site_retail_prices_price_check" CHECK ("price_minor" >= 0),
  CONSTRAINT "site_retail_prices_version_check" CHECK ("version" > 0),
  CONSTRAINT "site_retail_prices_site_id_fkey" FOREIGN KEY ("site_id") REFERENCES "sites"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "site_retail_prices_item_id_fkey" FOREIGN KEY ("item_id") REFERENCES "items"("id") ON DELETE RESTRICT ON UPDATE CASCADE
);

CREATE UNIQUE INDEX "site_retail_prices_site_id_item_id_key" ON "site_retail_prices"("site_id", "item_id");
CREATE INDEX "site_retail_prices_item_id_idx" ON "site_retail_prices"("item_id");

CREATE TABLE "site_retail_price_revisions" (
  "id" UUID NOT NULL,
  "site_id" UUID NOT NULL,
  "item_id" UUID NOT NULL,
  "price_minor" INTEGER NOT NULL,
  "version" INTEGER NOT NULL,
  "effective_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT "site_retail_price_revisions_pkey" PRIMARY KEY ("id"),
  CONSTRAINT "site_retail_price_revisions_price_check" CHECK ("price_minor" >= 0),
  CONSTRAINT "site_retail_price_revisions_version_check" CHECK ("version" > 0),
  CONSTRAINT "site_retail_price_revisions_site_id_fkey" FOREIGN KEY ("site_id") REFERENCES "sites"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "site_retail_price_revisions_item_id_fkey" FOREIGN KEY ("item_id") REFERENCES "items"("id") ON DELETE RESTRICT ON UPDATE CASCADE
);

CREATE UNIQUE INDEX "site_retail_price_revisions_site_id_item_id_version_key" ON "site_retail_price_revisions"("site_id", "item_id", "version");
CREATE INDEX "site_retail_price_revisions_site_id_effective_at_idx" ON "site_retail_price_revisions"("site_id", "effective_at");
CREATE INDEX "site_retail_price_revisions_item_id_effective_at_idx" ON "site_retail_price_revisions"("item_id", "effective_at");

-- Preserve current production behavior at rollout: every existing branch starts
-- with the price it already received, then future edits are isolated per site.
INSERT INTO "site_retail_prices" ("id", "site_id", "item_id", "price_minor", "version", "updated_at")
SELECT gen_random_uuid(), s."id", i."id", i."retail_price_minor", 1, CURRENT_TIMESTAMP
FROM "sites" s
CROSS JOIN "items" i
WHERE s."type" IN ('BRANCH_TYPE_1', 'BRANCH_TYPE_2');

INSERT INTO "site_retail_price_revisions" ("id", "site_id", "item_id", "price_minor", "version", "effective_at")
SELECT gen_random_uuid(), "site_id", "item_id", "price_minor", "version", "updated_at"
FROM "site_retail_prices";
