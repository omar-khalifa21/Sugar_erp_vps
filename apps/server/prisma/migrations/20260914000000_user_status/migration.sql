ALTER TABLE "users" ADD COLUMN "status" VARCHAR(30) NOT NULL DEFAULT 'ACTIVE';
ALTER TABLE "users" ADD CONSTRAINT "users_status_check" CHECK ("status" IN ('PENDING_PERMISSION', 'ACTIVE', 'DISABLED'));
UPDATE "users" SET "status" = 'DISABLED' WHERE "active" = false;
