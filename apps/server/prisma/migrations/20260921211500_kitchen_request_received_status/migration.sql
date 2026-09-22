ALTER TABLE "public"."kitchen_requests"
    DROP CONSTRAINT IF EXISTS "kitchen_requests_status_check";

ALTER TABLE "public"."kitchen_requests"
    ADD CONSTRAINT "kitchen_requests_status_check"
    CHECK ("status" IN ('REQUESTED','RECEIVED','PARTIAL','FULFILLED','REJECTED','CANCELLED') AND "version" > 0);
