# Sugar ERP integration report

Date: 2026-09-19

## Production deployment

- Deployed to `198.199.85.22` under `/opt/sugar-erp` and exposed through `https://ascendyz.xyz`.
- A verified PostgreSQL backup was taken before migration. All 12 migrations applied successfully.
- API, PostgreSQL, and Admin web containers are healthy; PostgreSQL/API/web ports remain bound to loopback and UFW exposes only SSH, HTTP, and HTTPS.
- The isolated VPS PostgreSQL integration suite passes: 10/10 tests.
- Hourly verified PostgreSQL backups are enabled through `sugar-postgres-backup.timer`.
- Branch Type 1 Desktop, Branch Type 1 Touch, Branch Type 2, and Kitchen installers version `1.0.0` are published in `/opt/sugar-erp/releases`. Every public download was streamed through HTTPS and matched its manifest SHA-256.

## Implemented

- PostgreSQL-backed sync ingestion is ordered, transactional, epoch-checked, hash-checked, dependency-aware, and idempotent. Desktop SQLite clients retain an inbox, outbox, cursor, retry state, and owning-site UUIDs.
- Branch Type 1, Branch Type 2, and Kitchen perform sync at startup and every 15–20 seconds. Local writes queue while offline and flush in device-sequence order after connectivity returns.
- Central projections cover requests, dispatches, counted receipts, quantity conflicts, Type 1 and Type 2 inventory, ingredient receipts/waste/counts, retail sales, sale corrections/refunds, cafe customers, cafe price revisions, cafe invoices/status/payments, and branch returns dispatched to Kitchen.
- Server-side stock validation prevents a duplicate receipt/sale/invoice from changing stock twice. Branch Type 2 inventory events must immediately follow and reconcile to their owning business document.
- Branch Type 1 cafe functions and catalog-management entry are disabled. Its central catalog remains a read-only synchronized cache.
- Admin conflict decisions use expected versions; the site applies a decision once and emits `quantity_conflict.applied`.
- All desktop applications use centralized build metadata for API URL, update URL, application profile, environment, and version. The production default is `https://ascendyz.xyz/api/v1`; development builds can override it without source changes.
- Device enrollment remains normal user/device authentication and records application version/last connection. No privileged server secret is embedded in a client.
- Per-profile release endpoints and manifests include semantic version, release date/notes, required flag, SHA-256, profile, signature status, size, and download URL.
- Desktop update checks run at startup and every six hours. Downloads are streamed with progress, restricted to the configured HTTPS server origin, and SHA-256 checked. Authenticode is verified when a signed release is published; unsigned releases remain supported for this private deployment. Updates are refused with an open shift or pending outbox, create an online SQLite backup, preserve ProgramData/AppData settings, launch NSIS silently, and relaunch.
- Installer settings preserve desktop-shortcut choice and Excel/report directory. Uninstall/update scripts do not delete SQLite databases or machine configuration.
- VPS deployment includes PostgreSQL backup, restore-verification, health-check, and systemd automation scripts.

## Database changes

Central PostgreSQL migrations add user status; transfer/request/shipment/receipt/conflict models; cafe origin/pricing/invoice/payment models; inventory ledger/balances/adjustments; Kitchen recipes/ingredients/variance; and immutable `sale_corrections` with JSONB line snapshots and source-event uniqueness.

Desktop SQLite additive upgrades add/retain outbox/inbox state, local-applied markers, Kitchen receipt/return caches, ingredient balances/movements, recipes, products, cafe/customer/order caches, and online pre-update backups. Existing local databases are upgraded in place.

## API surface

- `POST /api/v1/enrollment/enroll`
- `POST /api/v1/sync/push`
- `GET /api/v1/sync/pull`
- `POST /api/v1/sync/ack`
- `GET /api/v1/sync/bootstrap`
- Admin catalog, users/roles/sites/devices, cafe, recipes, stock adjustments, conflicts, and operational overview endpoints already exposed by their NestJS modules.
- Public, profile-specific release metadata/download endpoints under `/api/v1/releases/branch-type-1`, `/branch-type-1-touch`, `/branch-type-2`, and `/kitchen`.

## Duplicate and conflict controls

- Stable UUID plus content hash rejects same-ID/different-content reuse.
- `(device, stream epoch, sequence)` uniqueness and exact-next-sequence validation prevents skipping and replay.
- Business tables also have unique source-event/document keys.
- PostgreSQL serializable transactions store event, projection, and updated expected sequence together.
- SQLite transactions store business mutation and outbox event together; an `AppliedLocally` marker prevents server echo/retry from applying the local effect again.
- Cafe price lists use monotonically increasing versions and reject stale writers.
- Sale corrections are immutable, cannot exceed original line quantities, reconcile refund money, separate restock from discard, and update central stock only for explicit restock.

## Publishing a release

Build from `installers/<profile>/build-installer.ps1 -Version X.Y.Z -Production -ApiBaseUrl https://your-domain/api/v1`. Production signing configuration must be present. Publish the resulting signed `.exe` and `current.json` in the matching server release directory configured by the release controller. Never publish a manifest generated for another profile.

Increment the application semantic version passed to the build script, sign the application and installer, generate the manifest with `Write-ReleaseManifest.ps1`, deploy both atomically, then verify the profile's `/current` endpoint and checksum before rollout.

## Verification performed

- `dotnet build SugarERP.slnx -c Release --no-restore`: passed, 0 warnings/errors.
- `dotnet test tests/SugarERP.Branch1.Tests/SugarERP.Branch1.Tests.csproj -c Release --no-build`: 49 passed.
- Server `npm run build`, `npm run lint`, `npm test`: passed; 8 tests passed.
- Admin web `npm run build`: passed.
- Prisma generation and schema validation: passed.
- `git diff --check`: passed (line-ending notices only).

## Remaining production blockers

These require production infrastructure or unfinished workflow work and must not be waived:

1. Kitchen return receipt currently reaches Kitchen and is auditable, but the Kitchen count/disposition acknowledgement/dispute workflow and normalized central return-receipt tables are not complete. Do not treat branch returns as fully closed until this is implemented and tested.
2. Physical printer, touchscreen, network-loss/recovery on real branch PCs, clean-Windows installer rollback, and off-VPS encrypted backup restore drills need staging hardware/infrastructure acceptance.
3. The current dependency audit reports high-severity advisories in NestJS 11's multipart dependency and Prisma CLI transitive tooling. The application does not expose multipart upload routes and Prisma CLI is not used by the runtime API process, but a tested framework/toolchain upgrade is still required to clear the audit report.

Production acceptance is gated on clearing the three items above.
