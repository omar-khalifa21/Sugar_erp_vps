# Branch Type 1 handoff — Manual Wared

Date: 2026-09-27  
Implementation commit: `b8c267ae7bd1c1f7d81a7d1e78928eb4c7f3b071`

## Delivered

- The existing Branch 1 Kitchen request and shipment receipt workflow remains unchanged.
- Branch 1 Touch has a separate `إضافة وارد يدوي` page with item quantities, an explicit Kitchen-ingredient toggle, and a review/confirm step.
- Manual Wared always commits local Branch stock, shift incoming totals, stock movements, audit/outbox identity, and the sync event in the existing SQLite transaction.
- `manual_incoming.posted` now carries `source = MANUAL_BRANCH_ENTRY` and `deduct_kitchen_ingredients`.
- The server applies Branch stock plus any requested recipe-derived Kitchen ingredient deductions atomically in the existing sync transaction. Only selected product recipes are queried. Missing recipes and insufficient ingredients leave the server event unapplied and retryable.
- Admin Branch detail shows Manual Wared source, reason, document ID, and ingredient status.
- No central or local database schema migration was required.
- Manual Wared no longer asks an employee for a reason. The local operation stores the backward-compatible internal description `إضافة وارد يدوي` when no reason is supplied.
- Every Manual Wared row now has touch-sized `−` and `+` controls around the editable quantity. They change by one product unit and never reduce below zero.
- The previously non-responsive-looking `مراجعة الوارد` command was blocked solely by the mandatory reason validation before it could switch to the review state. Removing that validation allows the existing bound command to populate the review rows and reveal the confirmation page.

## Release

- Production profile: Branch Type 1 Touch only
- Version: `1.2.12`
- Artifact: `installers/branch-type-1/artifacts/branch-type-1-touch/Sugar-Branch-Type-1-Touch-1.2.12.exe`
- SHA-256: `a104b94f716aaf4917fcc6223a86a5c6d42684555ef40be63d1f7b4725ddc8b7`
- Signature status: unsigned internal (`signed: false`), as owner-authorized
- Public release path: `/opt/sugar-erp/releases/branch-type-1-touch/Sugar-Branch-Type-1-Touch-1.2.12.exe`
- Public updater endpoint: `https://ascendyz.xyz/api/v1/releases/branch-type-1/touch/current`
- The normal/non-touch Branch 1 installer was not built, packaged, or published in this task.

## Verification evidence

- Branch 1 Release tests: 93/93 passed, including the two-item Manual Wared ViewModel path: touch increments/decrement, review without a reason, confirmation, local balance changes, and queued outbox event.
- Server unit tests: 23/23 passed.
- Server PostgreSQL integration tests: 11/11 passed against a disposable isolated database on the VPS; the database was dropped after the run.
- Changed server files pass ESLint and server typecheck.
- Admin production build passed.
- Existing `KitchenRequestSyncFlowTests` passed.
- Touch executable 1.2.12 launch smoke stayed running until intentionally stopped.
- VPS backup verified before deploy: `sugar-20260927T112543Z.dump`.
- VPS migration job: 19 migrations found, no pending migrations.
- VPS API, Admin web, and PostgreSQL containers healthy after deploy.
- Public Touch manifest reports 1.2.12 and the actual downloaded file matched size `79,421,182` and the SHA-256 above.
- Downloaded installer reports product `Sugar ERP Branch1 Touch`, file version `1.2.12`.
- The real Touch updater class, configured as installed version 1.2.10, detected production version 1.2.11.

## Environment note

Docker Desktop was unavailable on the Windows workstation, so PostgreSQL integration was verified against a disposable isolated VPS database rather than locally. Production data was not used by the tests.

## Repository/deployment state

The implementation commit was fast-forwarded to the VPS checkout and deployed. It has not been pushed to `origin`; the VPS checkout is one commit ahead of `origin/main` until the owner authorizes a push.
