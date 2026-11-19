CREATE TABLE "kitchen_enrollment_settings" (
    "id" INTEGER PRIMARY KEY DEFAULT 1 CHECK ("id" = 1),
    "site_id" UUID NOT NULL UNIQUE REFERENCES "sites"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
    "password_hash" VARCHAR(255) NOT NULL,
    "updated_by" UUID NOT NULL,
    "updated_at" TIMESTAMPTZ(6) NOT NULL
);
