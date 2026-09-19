ALTER TABLE "cafe_customers"
  ADD COLUMN "origin_site_id" UUID,
  ADD COLUMN "source_event_id" UUID;

CREATE UNIQUE INDEX "cafe_customers_source_event_id_key" ON "cafe_customers"("source_event_id");
CREATE INDEX "cafe_customers_origin_site_id_idx" ON "cafe_customers"("origin_site_id");

ALTER TABLE "cafe_customers"
  ADD CONSTRAINT "cafe_customers_origin_site_id_fkey"
  FOREIGN KEY ("origin_site_id") REFERENCES "sites"("id") ON DELETE RESTRICT ON UPDATE CASCADE;
