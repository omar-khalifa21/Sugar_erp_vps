CREATE TABLE "pending_manual_deductions" (
  "source_event_id" UUID PRIMARY KEY REFERENCES "sync_events"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  "kitchen_site_id" UUID NOT NULL REFERENCES "sites"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  "retry_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX "pending_manual_deductions_retry_at_idx" ON "pending_manual_deductions"("retry_at");
