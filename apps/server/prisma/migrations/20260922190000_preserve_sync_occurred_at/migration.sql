ALTER TABLE "sync_events"
ADD COLUMN "occurred_at_raw" VARCHAR(64);

-- Hash verification uses the exact timestamp string sent by the owning device.
-- PostgreSQL timestamptz and JavaScript Date cannot preserve the seventh
-- fractional digit, so recover exact values from event payload snapshots where
-- the contract already carries the same operation timestamp.
UPDATE "sync_events"
SET "occurred_at_raw" = CASE
  WHEN "event_type" = 'kitchen_request.received' AND "payload" ? 'received_at'
    THEN "payload" ->> 'received_at'
  WHEN "event_type" = 'shipment.dispatched' AND "payload" ? 'dispatched_at'
    THEN "payload" ->> 'dispatched_at'
  ELSE to_char("occurred_at" AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"')
END;

ALTER TABLE "sync_events"
ALTER COLUMN "occurred_at_raw" SET NOT NULL;
