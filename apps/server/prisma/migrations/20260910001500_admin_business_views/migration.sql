CREATE TYPE "CafeInvoiceStatus" AS ENUM ('OPEN', 'PARTIALLY_PAID', 'PAID', 'REVERSED');
CREATE TYPE "ConflictStatus" AS ENUM ('OPEN', 'PENDING_SITE_APPLY', 'RESOLVED');
CREATE TYPE "DecisionApplicationStatus" AS ENUM ('CREATED', 'DELIVERED', 'APPLIED', 'REJECTED');

CREATE TABLE "cafe_customers" (
  "id" UUID NOT NULL, "code" VARCHAR(50) NOT NULL, "name" VARCHAR(200) NOT NULL,
  "active" BOOLEAN NOT NULL DEFAULT true, "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
  "updated_at" TIMESTAMPTZ(6) NOT NULL, CONSTRAINT "cafe_customers_pkey" PRIMARY KEY ("id")
);
CREATE UNIQUE INDEX "cafe_customers_code_key" ON "cafe_customers"("code");

CREATE TABLE "cafe_invoices" (
  "id" UUID NOT NULL, "customer_id" UUID NOT NULL, "issuing_site_id" UUID NOT NULL,
  "invoice_number" VARCHAR(100) NOT NULL, "business_date" DATE NOT NULL, "net_minor" BIGINT NOT NULL,
  "status" "CafeInvoiceStatus" NOT NULL DEFAULT 'OPEN', "occurred_at" TIMESTAMPTZ(6) NOT NULL,
  "source_event_id" UUID, "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT "cafe_invoices_pkey" PRIMARY KEY ("id"),
  CONSTRAINT "cafe_invoices_customer_id_fkey" FOREIGN KEY ("customer_id") REFERENCES "cafe_customers"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "cafe_invoices_issuing_site_id_fkey" FOREIGN KEY ("issuing_site_id") REFERENCES "sites"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "cafe_invoices_net_minor_check" CHECK ("net_minor" >= 0)
);
CREATE UNIQUE INDEX "cafe_invoices_source_event_id_key" ON "cafe_invoices"("source_event_id");
CREATE UNIQUE INDEX "cafe_invoices_issuing_site_id_invoice_number_key" ON "cafe_invoices"("issuing_site_id", "invoice_number");
CREATE INDEX "cafe_invoices_customer_id_status_business_date_idx" ON "cafe_invoices"("customer_id", "status", "business_date");

CREATE TABLE "cafe_payments" (
  "id" UUID NOT NULL, "customer_id" UUID NOT NULL, "collected_at_site_id" UUID NOT NULL,
  "reference" VARCHAR(100) NOT NULL, "amount_minor" BIGINT NOT NULL, "occurred_at" TIMESTAMPTZ(6) NOT NULL,
  "reversed_at" TIMESTAMPTZ(6), "source_event_id" UUID, "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT "cafe_payments_pkey" PRIMARY KEY ("id"),
  CONSTRAINT "cafe_payments_customer_id_fkey" FOREIGN KEY ("customer_id") REFERENCES "cafe_customers"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "cafe_payments_collected_at_site_id_fkey" FOREIGN KEY ("collected_at_site_id") REFERENCES "sites"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "cafe_payments_amount_minor_check" CHECK ("amount_minor" > 0)
);
CREATE UNIQUE INDEX "cafe_payments_reference_key" ON "cafe_payments"("reference");
CREATE UNIQUE INDEX "cafe_payments_source_event_id_key" ON "cafe_payments"("source_event_id");
CREATE INDEX "cafe_payments_customer_id_occurred_at_idx" ON "cafe_payments"("customer_id", "occurred_at");

CREATE TABLE "cafe_payment_allocations" (
  "id" UUID NOT NULL, "payment_id" UUID NOT NULL, "invoice_id" UUID NOT NULL,
  "amount_minor" BIGINT NOT NULL, "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT "cafe_payment_allocations_pkey" PRIMARY KEY ("id"),
  CONSTRAINT "cafe_payment_allocations_payment_id_fkey" FOREIGN KEY ("payment_id") REFERENCES "cafe_payments"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "cafe_payment_allocations_invoice_id_fkey" FOREIGN KEY ("invoice_id") REFERENCES "cafe_invoices"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "cafe_payment_allocations_amount_minor_check" CHECK ("amount_minor" > 0)
);
CREATE UNIQUE INDEX "cafe_payment_allocations_payment_id_invoice_id_key" ON "cafe_payment_allocations"("payment_id", "invoice_id");
CREATE INDEX "cafe_payment_allocations_invoice_id_idx" ON "cafe_payment_allocations"("invoice_id");

CREATE TABLE "quantity_conflicts" (
  "id" UUID NOT NULL, "site_id" UUID NOT NULL, "reference" VARCHAR(120) NOT NULL,
  "status" "ConflictStatus" NOT NULL DEFAULT 'OPEN', "version" INTEGER NOT NULL DEFAULT 1,
  "note" VARCHAR(500), "sent_at" TIMESTAMPTZ(6) NOT NULL, "counted_at" TIMESTAMPTZ(6) NOT NULL,
  "reported_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP, "source_event_id" UUID,
  CONSTRAINT "quantity_conflicts_pkey" PRIMARY KEY ("id"),
  CONSTRAINT "quantity_conflicts_site_id_fkey" FOREIGN KEY ("site_id") REFERENCES "sites"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "quantity_conflicts_version_check" CHECK ("version" > 0)
);
CREATE UNIQUE INDEX "quantity_conflicts_source_event_id_key" ON "quantity_conflicts"("source_event_id");
CREATE INDEX "quantity_conflicts_status_reported_at_idx" ON "quantity_conflicts"("status", "reported_at");
CREATE INDEX "quantity_conflicts_site_id_status_idx" ON "quantity_conflicts"("site_id", "status");

CREATE TABLE "conflict_lines" (
  "id" UUID NOT NULL, "conflict_id" UUID NOT NULL, "item_id" UUID NOT NULL,
  "sent_scaled" BIGINT NOT NULL, "counted_scaled" BIGINT NOT NULL, "final_scaled" BIGINT, "note" VARCHAR(500),
  CONSTRAINT "conflict_lines_pkey" PRIMARY KEY ("id"),
  CONSTRAINT "conflict_lines_conflict_id_fkey" FOREIGN KEY ("conflict_id") REFERENCES "quantity_conflicts"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "conflict_lines_item_id_fkey" FOREIGN KEY ("item_id") REFERENCES "items"("id") ON DELETE RESTRICT ON UPDATE CASCADE
);
CREATE UNIQUE INDEX "conflict_lines_conflict_id_item_id_key" ON "conflict_lines"("conflict_id", "item_id");

CREATE TABLE "admin_decisions" (
  "id" UUID NOT NULL, "conflict_id" UUID NOT NULL, "admin_user_id" UUID NOT NULL,
  "expected_version" INTEGER NOT NULL, "reason" VARCHAR(500) NOT NULL,
  "application_status" "DecisionApplicationStatus" NOT NULL DEFAULT 'CREATED',
  "decided_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP, "applied_at" TIMESTAMPTZ(6),
  CONSTRAINT "admin_decisions_pkey" PRIMARY KEY ("id"),
  CONSTRAINT "admin_decisions_conflict_id_fkey" FOREIGN KEY ("conflict_id") REFERENCES "quantity_conflicts"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "admin_decisions_admin_user_id_fkey" FOREIGN KEY ("admin_user_id") REFERENCES "users"("id") ON DELETE RESTRICT ON UPDATE CASCADE
);
CREATE UNIQUE INDEX "admin_decisions_conflict_id_key" ON "admin_decisions"("conflict_id");

CREATE TABLE "ingredient_variances" (
  "id" UUID NOT NULL, "site_id" UUID NOT NULL, "item_id" UUID NOT NULL, "business_date" DATE NOT NULL,
  "expected_scaled" BIGINT NOT NULL, "actual_scaled" BIGINT NOT NULL,
  "recorded_waste_scaled" BIGINT NOT NULL DEFAULT 0, "unexplained_variance_scaled" BIGINT NOT NULL,
  "cost_minor_per_scale" BIGINT, "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT "ingredient_variances_pkey" PRIMARY KEY ("id"),
  CONSTRAINT "ingredient_variances_site_id_fkey" FOREIGN KEY ("site_id") REFERENCES "sites"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "ingredient_variances_item_id_fkey" FOREIGN KEY ("item_id") REFERENCES "items"("id") ON DELETE RESTRICT ON UPDATE CASCADE
);
CREATE UNIQUE INDEX "ingredient_variances_site_id_item_id_business_date_key" ON "ingredient_variances"("site_id", "item_id", "business_date");
CREATE INDEX "ingredient_variances_site_id_business_date_idx" ON "ingredient_variances"("site_id", "business_date");
