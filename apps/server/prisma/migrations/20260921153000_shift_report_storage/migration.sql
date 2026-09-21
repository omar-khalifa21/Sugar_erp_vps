CREATE TABLE "shift_reports" (
    "id" UUID NOT NULL,
    "shift_id" UUID NOT NULL,
    "site_id" UUID NOT NULL,
    "device_id" UUID NOT NULL,
    "report_version" INTEGER NOT NULL,
    "business_date" DATE NOT NULL,
    "shift_kind" "ShiftKind" NOT NULL,
    "filename" VARCHAR(255) NOT NULL,
    "content_hash" CHAR(64) NOT NULL,
    "byte_length" BIGINT NOT NULL,
    "content" BYTEA NOT NULL,
    "uploaded_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "shift_reports_pkey" PRIMARY KEY ("id"),
    CONSTRAINT "shift_reports_report_version_check" CHECK ("report_version" > 0),
    CONSTRAINT "shift_reports_byte_length_check" CHECK ("byte_length" > 0)
);

CREATE UNIQUE INDEX "shift_reports_shift_id_report_version_key" ON "shift_reports"("shift_id", "report_version");
CREATE INDEX "shift_reports_site_id_business_date_idx" ON "shift_reports"("site_id", "business_date");

ALTER TABLE "shift_reports" ADD CONSTRAINT "shift_reports_site_id_fkey"
FOREIGN KEY ("site_id") REFERENCES "sites"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

ALTER TABLE "shift_reports" ADD CONSTRAINT "shift_reports_device_id_fkey"
FOREIGN KEY ("device_id") REFERENCES "devices"("id") ON DELETE RESTRICT ON UPDATE CASCADE;
