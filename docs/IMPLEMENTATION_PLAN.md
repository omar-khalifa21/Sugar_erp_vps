# Sugar ERP implementation plan

Last updated: 2026-09-10

## Completed foundation — Dockerized VPS vertical slice

Delivered the first runnable vertical slice: initialized the confirmed empty Git repository; scaffolded a pinned NestJS application; ran PostgreSQL through Docker Compose; managed the schema with Prisma; added the first migration; implemented health/readiness plus foundational `sites`, `devices`, `users`, `roles`, and `items` modules; added JWT authentication scaffolding; and verified it against the real PostgreSQL service.

Dependencies: Git, Docker Engine/Compose, Node 22/npm 11 for local tooling, access to `https://github.com/omar-khalifa21/Sugar_erp_vps.git`.

Acceptance criteria:

- `docker compose -f compose.dev.yml up --build` starts PostgreSQL and the API.
- `/api/v1/health` reports process health and `/api/v1/ready` verifies database connectivity and applied migrations.
- The initial migration creates constrained relational foundations for sites, devices, users, roles, role assignments, and items.
- Authentication can validate a seeded development administrator without committing a secret; protected endpoints reject anonymous access.
- Unit tests and integration tests against the Compose PostgreSQL container pass; lint, type-check, and production build pass.
- The verified milestone is committed, with no secrets, database data, build output, or generated reports tracked.

## Completed checkpoint — Contract v1 and usable Admin operations

Delivered the Arabic RTL React admin surface and the first frozen synchronization contract: device enrollment, idempotent push, cursor pull/ack, ordering/dependency checks, compatibility metadata, schemas/fixtures, and PostgreSQL deduplication tests. The Admin now has progressive branch drill-down, live stock and revenue, safe stock-adjustment requests, catalog CRUD with versioned EGP prices, user/role CRUD with friendly privilege selection, device enrollment, café entities with customer-specific prices and historical invoice lines, receivables, kitchen stock/variance views, and conflict decisions. The current local UI uses the confirmed pink-and-white cupcake direction and touch mode.

Dependencies: completed VPS foundation and its migration conventions.

Acceptance evidence: the responsive site builds and runs at `http://localhost:4173`; API requests reach the real PostgreSQL container; all test catalog items have prices; customer price changes do not rewrite invoice-line price snapshots; identical event replay ten times remains idempotent; same ID/different hash, sequence gaps, and missing dependencies are rejected correctly; unit, type, lint, production builds, and seven PostgreSQL integration tests pass.

## Current milestone — Branch Type 1 runnable Avalonia slice (In progress)

Build the first shared desktop foundation and a runnable Windows Avalonia Type 1 profile. Start with Arabic RTL/touch navigation, SQLite + EF Core migrations, safe first-run profile/site enrollment state, cached catalog with published prices, one SALEABLE stock location, an offline-capable shift/POS transaction, durable outbox records, and clear sync status. Type 1 contains no café/external-order code or UI.

Dependencies: Contract v1 checkpoint, .NET SDK 9, Avalonia packages, SQLite/EF Core, the local Nest API, and a Windows runtime.

Acceptance criteria: restore/build/test succeeds without global tooling; first run creates/migrates a durable SQLite database outside versioned binaries; an Arabic touchscreen flow can open a shift and commit a priced cash/Visa sale transactionally while offline; stock cannot become negative; receipt lines snapshot name/unit/price; double submission is idempotent; an outbox event is queued; restart retains the shift, sale, stock, and pending synchronization; the client launches locally for review.

## Later milestones (Pending)

Complete the remaining VPS/Admin business modules, then Branch Type 2, Kitchen, installers/update delivery, full integration, and production-preparation/CI. Each milestone is refined only when it becomes current and is completed with tests, documentation, and a scoped commit before the next begins.

## Decisions and assumptions

- Confirmed: NestJS/PostgreSQL central modular monolith; Prisma is the single server ORM/migration tool; versioned HTTPS JSON; Docker Compose; EGP integer minor units; scaled integer quantities; UUID identity; UTC timestamps with configurable `Africa/Cairo` business dates; one active stock-writing device per site in v1; `.xlsx` only; no Admin frontend until its framework is approved.
- Provisional and non-blocking now: disputed shipment goods remain on HOLD and the whole disputed shipment is unavailable; ingredient consumption occurs once at dispatch; after-closing sales are disabled; physical count differences require approval; initial cross-site coordination requires synchronization.
- Confirmed through the requested running redesign: React/Vite remains the Admin frontend for this monorepo; Admin can manage published catalog prices and café-specific overrides, while historical document lines keep price snapshots.
- Unresolved before affected production work: final correction authority, refund tip treatment, production naming/catalog data, printer hardware, final units/precision policy, offline-grant TTL, backup retention/RPO/RTO, installer signing credentials, DNS/TLS, and live VPS access.
- Confirmed 2026-09-09: the VPS retains the complete synchronized operational record and will include independent backup/restore workflows; unsynchronized local events remain a separately disclosed recovery risk. Branch Type 1, Branch Type 2, and Kitchen installers will expose a touch-screen-optimized configuration option while sharing the same profile codebase.
- Current blockers: none.
