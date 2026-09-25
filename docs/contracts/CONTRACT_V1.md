# Sugar ERP Contract v1

Status: frozen foundation for desktop implementation  
Version: `1.0`  
Date: 2026-09-09

## Transport and compatibility

- HTTPS JSON under `/api/v1`; UTF-8, UUID identities, UTC ISO-8601 timestamps, and integers for EGP minor units and scaled quantities.
- Clients send `contract_version: "1.0"`; the server returns current/minimum compatibility metadata. Additive fields are allowed, but removing or changing meaning requires a new major contract and migration plan.
- A device has one site, one profile, one active writer role, one credential, and a monotonically increasing `stream_epoch`. Restoring an old device backup requires a newly authorized epoch.
- Sync batches contain at most 100 events. Pull pages contain at most 100 events and use a signed opaque cursor bound to the enrolled device.

## Identity, enrollment, and permissions

An administrator creates an active site and issues a single-use enrollment token valid for 5–1440 minutes. Enrollment validates the site profile, refuses a second active writer, stores only hashes of the token and device credential, and returns the credential once. Revocation clears the credential, disables the writer, and increments its epoch.

Admin JWTs govern central administration. Device sync uses `X-Device-Id` and `X-Device-Secret` over HTTPS. No desktop receives PostgreSQL credentials. Profile and site authorization is derived from enrollment, never trusted from event payloads.

Permissions in v1:

| Actor | Scope |
|---|---|
| Admin | All sites, users, catalog, revenue, cafe, kitchen, conflicts, reports, releases, backup status, and private audit |
| Branch Type 1 writer | Own Type 1 operations including cafe/customer profiles, custom orders, collections, and cafe issue from SALEABLE stock; read-only freshness-marked branch stock projections; no conflict decision |
| Branch Type 2 writer | Own Type 2 operations including freezer/display and cafe issue; no conflict decision |
| Kitchen writer | Own kitchen ingredients, dispatch, waste, counts, and cafe issue; no conflict decision |

## Event envelope and integrity

The normative envelope is `packages/contracts/schemas/sync-event.schema.json`. SHA-256 is computed over canonical JSON containing, in lexical key order, `id`, `device_sequence`, `event_type`, `schema_version`, `occurred_at`, `payload`, and `dependencies` (default `[]`). Object keys are recursively sorted; array order is preserved; no whitespace is emitted.

The server transactionally verifies the credential, epoch, content hash, exact next sequence, and dependencies before storing an immutable event. Site/device fields are supplied by server enrollment. Replaying the same event id and hash returns `duplicate` without another business effect. Reusing the id for different content is a permanent integrity failure.

## Push, pull, and acknowledgement

- `POST /api/v1/sync/push`: atomic ordered batch. A sequence gap or missing dependency rejects the batch as retryable; malformed, unauthorized, stale-epoch, hash-conflicting, or sequence-reuse input is permanent until corrected or re-enrolled.
- `GET /api/v1/sync/pull?cursor=&limit=`: stable ascending server positions, permission-scoped to the device. Clients commit a page and its cursor in one SQLite transaction. Echo events may be skipped for business effects but are still acknowledged.
- `POST /api/v1/sync/ack`: monotonically records the applied signed cursor. An older acknowledgement is harmless.
- Retry uses bounded exponential backoff with jitter. WebSockets may wake polling but never replace durable pull.

## State machines

- Device: `PENDING -> ENROLLED -> REVOKED`; a recovery creates a new credential/epoch, never reactivates a copied database silently.
- Shipment: `REQUESTED -> DISPATCHED -> RECEIVED` or `DISPUTED -> DECIDED -> PENDING_SITE_APPLY -> RESOLVED`.
- Disputed goods remain on HOLD and unavailable. The entire disputed shipment is held until the administrator records per-line final quantities and a reason.
- Shift: `OPEN -> CLOSING -> CLOSED`; original close snapshots and XLSX bytes are immutable. Late corrections and conflict resolutions are supplements linked to the original.
- Admin command: `CREATED -> DELIVERED -> APPLIED` or `REJECTED`; a decision is not complete until the writer acknowledges application.

## Errors

Every error follows `packages/contracts/schemas/api-error.schema.json` with a safe message and correlation id. Key stable codes are `CONTENT_HASH_MISMATCH`, `IDEMPOTENCY_KEY_REUSE`, `SEQUENCE_GAP`, `SEQUENCE_REPLAY`, `DEPENDENCY_NOT_READY`, `STREAM_EPOCH_MISMATCH`, `DEVICE_NOT_BOOTSTRAPPED`, `ACTIVE_WRITER_EXISTS`, `STALE_VERSION`, and `BUSINESS_RULE_VIOLATION`.

HTTP mapping: 401 identity, 403 scope, 409 state/sequence/dependency, 422 semantic validation, 429 throttling, and 5xx transient server failures. Only explicitly marked failures are retryable.

## Business invariants carried by events

- Immutable facts are never overwritten. Corrections are linked deltas/reversals with actor, reason, source and posting time.
- Stock effects occur exactly once at the owning writer. VPS ingestion mirrors/project events and never performs a second deduction.
- Type 2 freezer/display transfers conserve total site quantity. Type 1 has only SALEABLE stock.
- Kitchen recipe consumption is captured once at dispatch using a recipe snapshot. Finished-product returns do not reverse raw ingredients.
- Retail net revenue, cafe invoices, cafe collections, outstanding receivables, and tips remain separate. Internal shipments are not revenue; no profit is claimed without complete cost/expense coverage.
- All read models expose `as_of` freshness. Stale/offline data is labeled and never presented as real time.

## Normative artifacts

- OpenAPI: `docs/contracts/openapi-v1.yaml`
- Event/error JSON Schemas: `packages/contracts/schemas/`
- Deterministic fixtures: `packages/contracts/fixtures/`
- PostgreSQL constraints and idempotency: migration `20260909224500_contract_v1_sync`

Contract changes after this checkpoint require a versioned ADR, compatibility classification, fixtures, and migration/test updates.
