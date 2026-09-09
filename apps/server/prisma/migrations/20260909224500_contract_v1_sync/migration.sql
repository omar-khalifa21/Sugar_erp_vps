ALTER TABLE "devices"
  ADD COLUMN "name" VARCHAR(120),
  ADD COLUMN "credential_hash" CHAR(64);

CREATE UNIQUE INDEX "devices_credential_hash_key" ON "devices"("credential_hash");

CREATE TABLE "enrollment_tokens" (
  "id" UUID NOT NULL,
  "site_id" UUID NOT NULL,
  "profile" "DeviceProfile" NOT NULL,
  "token_hash" CHAR(64) NOT NULL,
  "expires_at" TIMESTAMPTZ(6) NOT NULL,
  "used_at" TIMESTAMPTZ(6),
  "used_by_device_id" UUID,
  "created_by_user_id" UUID NOT NULL,
  "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT "enrollment_tokens_pkey" PRIMARY KEY ("id"),
  CONSTRAINT "enrollment_tokens_site_id_fkey" FOREIGN KEY ("site_id") REFERENCES "sites"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "enrollment_tokens_expiry_check" CHECK ("expires_at" > "created_at")
);

CREATE UNIQUE INDEX "enrollment_tokens_token_hash_key" ON "enrollment_tokens"("token_hash");
CREATE INDEX "enrollment_tokens_site_id_expires_at_idx" ON "enrollment_tokens"("site_id", "expires_at");

CREATE TABLE "device_sync_states" (
  "device_id" UUID NOT NULL,
  "next_expected_sequence" INTEGER NOT NULL DEFAULT 1,
  "last_accepted_at" TIMESTAMPTZ(6),
  "updated_at" TIMESTAMPTZ(6) NOT NULL,
  CONSTRAINT "device_sync_states_pkey" PRIMARY KEY ("device_id"),
  CONSTRAINT "device_sync_states_device_id_fkey" FOREIGN KEY ("device_id") REFERENCES "devices"("id") ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT "device_sync_states_sequence_check" CHECK ("next_expected_sequence" > 0)
);

CREATE TABLE "device_sync_cursors" (
  "device_id" UUID NOT NULL,
  "last_server_position" BIGINT NOT NULL DEFAULT 0,
  "acknowledged_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT "device_sync_cursors_pkey" PRIMARY KEY ("device_id"),
  CONSTRAINT "device_sync_cursors_device_id_fkey" FOREIGN KEY ("device_id") REFERENCES "devices"("id") ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT "device_sync_cursors_position_check" CHECK ("last_server_position" >= 0)
);

CREATE TABLE "sync_events" (
  "id" UUID NOT NULL,
  "device_id" UUID NOT NULL,
  "site_id" UUID NOT NULL,
  "stream_epoch" INTEGER NOT NULL,
  "device_sequence" INTEGER NOT NULL,
  "event_type" VARCHAR(120) NOT NULL,
  "schema_version" INTEGER NOT NULL,
  "occurred_at" TIMESTAMPTZ(6) NOT NULL,
  "received_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
  "payload" JSONB NOT NULL,
  "dependencies" JSONB NOT NULL DEFAULT '[]'::jsonb,
  "content_hash" CHAR(64) NOT NULL,
  "server_position" BIGSERIAL NOT NULL,
  CONSTRAINT "sync_events_pkey" PRIMARY KEY ("id"),
  CONSTRAINT "sync_events_device_id_fkey" FOREIGN KEY ("device_id") REFERENCES "devices"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "sync_events_site_id_fkey" FOREIGN KEY ("site_id") REFERENCES "sites"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "sync_events_stream_epoch_check" CHECK ("stream_epoch" > 0),
  CONSTRAINT "sync_events_device_sequence_check" CHECK ("device_sequence" > 0),
  CONSTRAINT "sync_events_schema_version_check" CHECK ("schema_version" > 0),
  CONSTRAINT "sync_events_content_hash_check" CHECK ("content_hash" ~ '^[0-9a-f]{64}$')
);

CREATE UNIQUE INDEX "sync_events_server_position_key" ON "sync_events"("server_position");
CREATE UNIQUE INDEX "sync_events_device_id_stream_epoch_device_sequence_key" ON "sync_events"("device_id", "stream_epoch", "device_sequence");
CREATE INDEX "sync_events_site_id_server_position_idx" ON "sync_events"("site_id", "server_position");
CREATE INDEX "sync_events_event_type_occurred_at_idx" ON "sync_events"("event_type", "occurred_at");
