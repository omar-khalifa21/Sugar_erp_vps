CREATE EXTENSION IF NOT EXISTS "pgcrypto";

CREATE TYPE "SiteType" AS ENUM ('BRANCH_TYPE_1', 'BRANCH_TYPE_2', 'KITCHEN');
CREATE TYPE "DeviceProfile" AS ENUM ('BRANCH_TYPE_1', 'BRANCH_TYPE_2', 'KITCHEN');
CREATE TYPE "EnrollmentStatus" AS ENUM ('PENDING', 'ENROLLED', 'REVOKED');
CREATE TYPE "ItemKind" AS ENUM ('PRODUCT', 'INGREDIENT');

CREATE TABLE "sites" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "code" VARCHAR(50) NOT NULL,
    "name" VARCHAR(200) NOT NULL,
    "type" "SiteType" NOT NULL,
    "timezone" VARCHAR(100) NOT NULL DEFAULT 'Africa/Cairo',
    "active" BOOLEAN NOT NULL DEFAULT true,
    "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT "sites_pkey" PRIMARY KEY ("id"),
    CONSTRAINT "sites_code_nonempty" CHECK (length(btrim("code")) > 0),
    CONSTRAINT "sites_name_nonempty" CHECK (length(btrim("name")) > 0)
);

CREATE UNIQUE INDEX "sites_code_key" ON "sites"("code");

CREATE TABLE "devices" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "site_id" UUID NOT NULL,
    "profile" "DeviceProfile" NOT NULL,
    "enrollment_status" "EnrollmentStatus" NOT NULL DEFAULT 'PENDING',
    "key_thumbprint" VARCHAR(128),
    "active_writer" BOOLEAN NOT NULL DEFAULT false,
    "last_seen_at" TIMESTAMPTZ(6),
    "app_version" VARCHAR(50),
    "stream_epoch" INTEGER NOT NULL DEFAULT 1,
    "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT "devices_pkey" PRIMARY KEY ("id"),
    CONSTRAINT "devices_stream_epoch_positive" CHECK ("stream_epoch" > 0),
    CONSTRAINT "devices_site_fkey" FOREIGN KEY ("site_id") REFERENCES "sites"("id") ON DELETE RESTRICT ON UPDATE CASCADE
);

CREATE UNIQUE INDEX "devices_key_thumbprint_key" ON "devices"("key_thumbprint");
CREATE INDEX "devices_site_id_idx" ON "devices"("site_id");
CREATE UNIQUE INDEX "devices_one_active_writer_per_site" ON "devices"("site_id") WHERE "active_writer" = true AND "enrollment_status" = 'ENROLLED';

CREATE TABLE "users" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "username" VARCHAR(100) NOT NULL,
    "display_name" VARCHAR(200) NOT NULL,
    "password_hash" VARCHAR(255) NOT NULL,
    "active" BOOLEAN NOT NULL DEFAULT true,
    "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT "users_pkey" PRIMARY KEY ("id"),
    CONSTRAINT "users_username_normalized" CHECK ("username" = lower(btrim("username"))),
    CONSTRAINT "users_username_nonempty" CHECK (length("username") > 0)
);

CREATE UNIQUE INDEX "users_username_key" ON "users"("username");

CREATE TABLE "roles" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "code" VARCHAR(50) NOT NULL,
    "name" VARCHAR(100) NOT NULL,
    "permissions" TEXT[] NOT NULL DEFAULT ARRAY[]::TEXT[],
    "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT "roles_pkey" PRIMARY KEY ("id"),
    CONSTRAINT "roles_code_normalized" CHECK ("code" = upper(btrim("code")))
);

CREATE UNIQUE INDEX "roles_code_key" ON "roles"("code");

CREATE TABLE "user_site_roles" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "user_id" UUID NOT NULL,
    "site_id" UUID,
    "role_id" UUID NOT NULL,
    "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT "user_site_roles_pkey" PRIMARY KEY ("id"),
    CONSTRAINT "user_site_roles_user_fkey" FOREIGN KEY ("user_id") REFERENCES "users"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT "user_site_roles_site_fkey" FOREIGN KEY ("site_id") REFERENCES "sites"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT "user_site_roles_role_fkey" FOREIGN KEY ("role_id") REFERENCES "roles"("id") ON DELETE RESTRICT ON UPDATE CASCADE
);

CREATE UNIQUE INDEX "user_site_roles_user_id_site_id_role_id_key" ON "user_site_roles"("user_id", "site_id", "role_id");
CREATE UNIQUE INDEX "user_site_roles_global_unique" ON "user_site_roles"("user_id", "role_id") WHERE "site_id" IS NULL;
CREATE INDEX "user_site_roles_site_id_idx" ON "user_site_roles"("site_id");
CREATE INDEX "user_site_roles_role_id_idx" ON "user_site_roles"("role_id");

CREATE TABLE "items" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "sku" VARCHAR(100) NOT NULL,
    "name_ar" VARCHAR(300) NOT NULL,
    "unit" VARCHAR(30) NOT NULL,
    "quantity_scale" INTEGER NOT NULL,
    "kind" "ItemKind" NOT NULL,
    "active" BOOLEAN NOT NULL DEFAULT true,
    "version" INTEGER NOT NULL DEFAULT 1,
    "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT "items_pkey" PRIMARY KEY ("id"),
    CONSTRAINT "items_sku_nonempty" CHECK (length(btrim("sku")) > 0),
    CONSTRAINT "items_name_ar_nonempty" CHECK (length(btrim("name_ar")) > 0),
    CONSTRAINT "items_quantity_scale_positive" CHECK ("quantity_scale" > 0),
    CONSTRAINT "items_version_positive" CHECK ("version" > 0)
);

CREATE UNIQUE INDEX "items_sku_key" ON "items"("sku");
