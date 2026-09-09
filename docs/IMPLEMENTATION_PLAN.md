# Sugar ERP implementation plan

Last updated: 2026-09-09

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

## Current milestone — Admin web and Contract v1 synchronization (In progress)

Ship the Arabic RTL React admin website as the working VPS surface, then freeze the language-neutral API/event contract progressively: common types and errors, device enrollment, idempotent push, cursor pull, outbox/inbox semantics, compatibility metadata, state machines, OpenAPI/JSON Schema fixtures, and PostgreSQL-backed deduplication tests. Replace honest empty states in the admin with server read models as operational events become available.

Dependencies: completed VPS foundation and its migration conventions.

Acceptance criteria: the responsive admin website runs through Compose, authenticates against the API, exposes touch mode and all required navigation areas, and manages the live foundation data; contract schemas and examples validate; identical event replay is idempotent; same ID with different hash fails integrity checks; sequence/dependency gaps are retryable; authorization is site/profile scoped; contract and PostgreSQL integration tests pass.

## Later milestones (Pending)

Implement the remaining VPS business modules, then Branch Type 1, Branch Type 2, Kitchen, the owner-approved Admin frontend, installers/update delivery, full integration, and production-preparation/CI. Each milestone is refined only when it becomes current and is completed with tests, documentation, and a scoped commit before the next begins.

## Decisions and assumptions

- Confirmed: NestJS/PostgreSQL central modular monolith; Prisma is the single server ORM/migration tool; versioned HTTPS JSON; Docker Compose; EGP integer minor units; scaled integer quantities; UUID identity; UTC timestamps with configurable `Africa/Cairo` business dates; one active stock-writing device per site in v1; `.xlsx` only; no Admin frontend until its framework is approved.
- Provisional and non-blocking now: disputed shipment goods remain on HOLD and the whole disputed shipment is unavailable; ingredient consumption occurs once at dispatch; after-closing sales are disabled; physical count differences require approval; initial cross-site coordination requires synchronization.
- Unresolved before affected production work: Admin frontend framework, detailed correction/price permissions, refund tip treatment, production naming/catalog data, printer hardware, final units/precision policy, offline-grant TTL, backup retention/RPO/RTO, installer signing credentials, DNS/TLS, and live VPS access.
- Confirmed 2026-09-09: the VPS retains the complete synchronized operational record and will include independent backup/restore workflows; unsynchronized local events remain a separately disclosed recovery risk. Branch Type 1, Branch Type 2, and Kitchen installers will expose a touch-screen-optimized configuration option while sharing the same profile codebase.
- Current blockers: none.
