# VPS foundation checkpoint

Checkpoint commit: `ed5e741` on `main` (pushed to the confirmed GitHub remote).

The NestJS/PostgreSQL foundation, Contract v1 enrollment and sync endpoints, live React Admin surface, catalog pricing, user/role management, branch read models, café receivables/prices, kitchen variance view, and Admin conflict decisions are available. PostgreSQL migrations through `20260910004500_catalog_and_cafe_pricing` are applied locally.

Local development endpoints:

- Admin: `http://localhost:4173/`
- API health: `http://localhost:3001/api/v1/health` for the current host build, or port `3000` for Compose
- API base: `/api/v1`

Verified at handoff: server and Admin type-check/build, server lint/unit test, 7 real-PostgreSQL integration tests, Compose configuration, and live proxy smoke checks. The desktop client must preserve Contract v1 UUIDs, scaled integer quantities, EGP minor units, device sequence/epoch ordering, canonical SHA-256 hashes, and idempotent outbox retry behavior. Branch Type 1 must not contain café/external-order capabilities.
