# Sugar ERP repository instructions

Read `docs/agent-prompts/SHARED_PROJECT_CONTEXT.md` and the prompt for your assigned module before editing code. If `docs/contracts/CONTRACT_V1.md` exists, it is the authoritative integration contract.

## Fixed stack

- Central API/worker: TypeScript + NestJS
- Central database: PostgreSQL
- VPS runtime and local server development: Docker Compose
- Desktop applications: C# + Avalonia on Windows
- Desktop local database: SQLite + Entity Framework Core
- Transport: versioned HTTPS JSON APIs
- Reports: Excel `.xlsx` only; no CSV
- CI/CD: GitHub Actions
- Architecture: one monorepo and a modular monolith on the VPS

The Admin frontend framework is unresolved. Do not implement it until the owner approves a choice.

## Non-negotiable invariants

- Branch Type 1 has no external/cafe orders.
- Branch Type 2 and Kitchen support external/cafe orders.
- Only Admin resolves quantity conflicts.
- Every posted stock or money change is immutable, auditable, and reversed by another transaction—not edited away.
- Closed shifts and their original `.xlsx` reports are immutable.
- UUIDs are created at the owning site and stay identical on the server.
- Sync is outbox/inbox based, versioned, retryable, ordered where required, and idempotent.
- Synchronization must never duplicate a sale, receipt, payment, shipment, ingredient deduction, or stock movement.
- Kitchen deducts recipe ingredients once at dispatch under the baseline design; branch receipt and conflict resolution never deduct them again.
- Never expose PostgreSQL publicly or place secrets in Git, images, installers, or desktop configuration.
- One active stock-writing device per site in v1.

## Ownership

- VPS chat owns `apps/server`, `apps/admin-web`, `packages/contracts`, central migrations, `deploy`, and shared contract documents until Contract v1 freezes.
- Branch Type 1 owns `apps/branch-type-1`, its tests, and `installers/branch-type-1`.
- Branch Type 2 owns `apps/branch-type-2`, its tests, and `installers/branch-type-2`.
- Kitchen owns `apps/kitchen`, its tests, and `installers/kitchen`.
- Shared contract changes after freeze require an ADR and coordinator approval.

Never modify another chat's owned area. Keep commits small and scoped. Never push, deploy, rewrite history, or use real secrets without explicit authorization.

