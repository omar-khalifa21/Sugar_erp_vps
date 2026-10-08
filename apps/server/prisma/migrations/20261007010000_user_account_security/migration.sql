BEGIN;
SET LOCAL lock_timeout = '15s';

-- The previous user-status migration already backfilled existing users as ACTIVE.
-- Do not approve existing pending registrations or reactivate disabled users.
ALTER TABLE "users" ADD COLUMN "token_version" INTEGER NOT NULL DEFAULT 0;

-- Preserve immutable business attribution without retaining a login account.
CREATE TABLE "user_identities" (
  "id" UUID PRIMARY KEY,
  "display_name" VARCHAR(200) NOT NULL
);
INSERT INTO "user_identities" ("id", "display_name") SELECT "id", "display_name" FROM "users";
ALTER TABLE "inventory_transactions" DROP CONSTRAINT "inventory_transactions_user_id_fkey";
ALTER TABLE "inventory_transactions" ADD CONSTRAINT "inventory_transactions_user_id_fkey"
  FOREIGN KEY ("user_id") REFERENCES "user_identities"("id") ON DELETE RESTRICT ON UPDATE CASCADE;
ALTER TABLE "stock_adjustments" DROP CONSTRAINT "stock_adjustments_created_by_user_id_fkey";
ALTER TABLE "stock_adjustments" ADD CONSTRAINT "stock_adjustments_created_by_user_id_fkey"
  FOREIGN KEY ("created_by_user_id") REFERENCES "user_identities"("id") ON DELETE RESTRICT ON UPDATE CASCADE;
ALTER TABLE "admin_decisions" DROP CONSTRAINT "admin_decisions_admin_user_id_fkey";
ALTER TABLE "admin_decisions" ADD CONSTRAINT "admin_decisions_admin_user_id_fkey"
  FOREIGN KEY ("admin_user_id") REFERENCES "user_identities"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- Covers signup, admin creation, bootstrap, and older application writers.
CREATE FUNCTION maintain_user_identity() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  INSERT INTO "user_identities" ("id", "display_name") VALUES (NEW."id", NEW."display_name")
    ON CONFLICT ("id") DO UPDATE SET "display_name" = EXCLUDED."display_name";
  RETURN NEW;
END;
$$;
CREATE TRIGGER users_identity AFTER INSERT OR UPDATE OF "display_name" ON "users"
  FOR EACH ROW EXECUTE FUNCTION maintain_user_identity();

CREATE FUNCTION invalidate_user_tokens() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  IF NEW."password_hash" IS DISTINCT FROM OLD."password_hash"
    OR NEW."status" IS DISTINCT FROM OLD."status"
    OR NEW."active" IS DISTINCT FROM OLD."active" THEN
    NEW."token_version" := OLD."token_version" + 1;
  END IF;
  RETURN NEW;
END;
$$;
CREATE TRIGGER users_invalidate_tokens BEFORE UPDATE ON "users"
  FOR EACH ROW EXECUTE FUNCTION invalidate_user_tokens();

-- Snapshots deliberately survive deletion of either participant.
CREATE TABLE "user_audit_events" (
  "id" UUID PRIMARY KEY,
  "actor_id" UUID NOT NULL,
  "actor_username" VARCHAR(100) NOT NULL,
  "target_id" UUID NOT NULL,
  "target_username" VARCHAR(100) NOT NULL,
  "action" VARCHAR(40) NOT NULL,
  "occurred_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX "user_audit_events_target_id_occurred_at_idx" ON "user_audit_events"("target_id", "occurred_at");
CREATE FUNCTION preserve_user_audit() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'User audit events are immutable';
END;
$$;
CREATE TRIGGER user_audit_immutable BEFORE UPDATE OR DELETE ON "user_audit_events"
  FOR EACH ROW EXECUTE FUNCTION preserve_user_audit();

COMMIT;
