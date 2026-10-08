# Admin account security review — 7–8 October 2026

Scope: central API, React administration, Prisma schema/migrations, deployment configuration; source-level security sweep across desktop/shared C# code, installers and package manifests. Desktop-owned code was not changed. This is a targeted code review and regression exercise, not a penetration-test certificate.

## Findings recorded before remediation

Locations describe the original checkout; line numbers can move in this change.

| Severity | Location | Finding and treatment |
| --- | --- | --- |
| High | `src/auth/auth.service.ts:70`, `src/auth/jwt.strategy.ts:31` | Pending users received JWTs and passed authentication. Require ACTIVE + active on login, session access and every bearer request. Old pending JWTs fail too. |
| High | `src/auth/auth.controller.ts`, `src/configure-app.ts` | No authentication throttling; login/signup were exposed to automated guesses and expensive password hashing. Bound login to 20 attempts/minute/IP and signup to 5/10 minutes/IP; cap authentication bodies at 16 KiB and passwords at 1,024 characters. |
| High | `src/users/users.service.ts`, `src/roles/roles.service.ts` | Final administrator could be disabled/demoted, or ADMIN role disabled/renamed. Serialize administrative removals under the ADMIN row lock, recheck the actor inside the transaction, require an active admin after changes, and protect the reserved ADMIN role. |
| High | `src/sync/sync.service.ts:80`, `src/sync/transfer-projection.service.ts:89` | Device-submitted `catalog.item_published` bypassed projection validation and entered shared catalog feeds. Reject server-owned catalog publication, conflict decisions, stock projections and device bootstrap events at device ingestion. Supported desktop event payloads remain unchanged. |
| Medium | `src/auth/auth.service.ts:43` | Duplicate signup exposed registered usernames; nonexistent login skipped password hashing. Return the same signup response and hash for both paths; verify a dummy password hash when login has no matching user. Pending status is disclosed only after verifying the correct password. |
| Medium | `src/users/users.service.ts:48`, `src/auth/jwt.strategy.ts` | Password resets did not revoke JWTs. Add token version, automatically increment on password/status/active changes, and validate it per request. Versionless pre-upgrade tokens remain valid only at version zero for approved accounts. Role reassignment also revokes tokens. |
| Medium | `src/sync/device-auth.service.ts:16` | Device credentials remained usable for disabled sites/non-writing devices. Check active site and active writer, plus enrollment and credential hash. |
| Medium | `src/reports/reports.service.ts:134` | Four ZIP magic bytes passed XLSX validation. Validate bounded ZIP directory/local-entry structure and required OpenXML parts; reject encryption, traversal names, duplicate entries, VBA payloads and excessive declared expansion. No extraction or untrusted XML parsing. |
| Medium | `deploy/nginx/app.conf`, `src/configure-app.ts` | Missing framing, CSP and API cache controls. Add browser policy headers at the HTTPS proxy and no-store/nosniff/frame denial on the API. Disable Express fingerprint header. |
| Feature gap | `src/users/users.controller.ts:27`, `prisma/schema.prisma:131` | DELETE only disabled users; no permanent deletion, explicit approval/rejection or deletion audit. Add dedicated routes while retaining the existing disable route. |
| Critical/high/moderate/low | root `package-lock.json`, server/admin `package.json` | Initial npm audit: 62 affected package entries: 1 critical, 49 high, 11 moderate, 1 low. Critical advisory: proxy-addr IP spoofing. High advisories included Vite file reads, Multer DoS and build/test tooling dependencies. Package remediation and final results are recorded below. |

Primary advisory references: [proxy-addr](https://github.com/advisories/GHSA-jqcg-44mw-7w3h), [Vite Windows file access](https://github.com/vitejs/vite/security/advisories/GHSA-fx2h-pf6j-xcff), [Vite WebSocket file access](https://github.com/vitejs/vite/security/advisories/GHSA-p9ff-h696-f583). Proxy configuration follows [Express trust-proxy guidance](https://expressjs.com/en/guide/behind-proxies/).

## Account lifecycle and compatibility

- Public signup remains public and returns `PENDING_PERMISSION` without a token. Pending users cannot log in or use bearer-protected endpoints. There are no refresh-token endpoints, OAuth/social providers, password recovery endpoints, or persisted user-session/token tables in this codebase.
- Approved users stay approved. The earlier `20260914000000_user_status` migration already backfilled existing active users to ACTIVE; this migration never approves pending users or reactivates disabled users. Admin-created users are explicitly approved, preserving the existing workflow.
- Rejection hard-deletes a pending account. Subsequent login returns the same generic credential error as a missing account, avoiding an existence oracle. A previously rejected username can submit another pending application; rejection is not a ban list.
- Approval selects an active role and optional active site. The existing admin grant-role/update route retains its approval behavior, now audited. The dedicated approval route refuses already-processed accounts.
- Permanent deletion requires the exact current username on the server. The browser cannot bypass it. Anonymous/non-admin callers fail authorization; self-deletion fails; admin removal/demotion/disabling is serialized to prevent concurrent lockouts.
- Deletion and its audit event commit together. Remove the actual `users` row, password hash, role assignments, and enrollment tokens issued by that user. A JWT for a missing user fails immediately, and recreating a username gives a new UUID.
- The only user-owned dependents are role assignments and enrollment-token credentials. There are no posts, avatars or user-owned filesystem uploads to delete. XLSX reports belong to sites/devices and must remain immutable. Enrolled device credentials belong to sites, not their enrolling administrator, so existing devices keep working after an admin account is removed.
- Three immutable business relationships (inventory transactions, stock adjustments, admin decisions) now reference `user_identities`: UUID and display name only, without authentication or permissions. Migration backfills this table, and a trigger maintains it for signup/admin creation/bootstrap. Transaction IDs, actor UUIDs, amounts and posted history remain unchanged. Delete an unused identity; retain one referenced by business history. Audit stores actor/target UUID and username snapshots independently and prohibits UPDATE/DELETE through a database trigger.
- Offline device operations retain their existing trust model. Approved operators may have device-bound, short-lived offline proofs; offline revocation cannot be instantaneous. Device ingestion checks already enforced active/approved operators for Branch Type 2 inventory. The remaining operational feeds use enrolled writer credentials under Contract v1; this change does not invent a new offline user-auth protocol or discard unsynchronized business events.

## Other reviewed surfaces

- Controllers default to JWT authentication; administration controllers require ADMIN server-side. Public routes are login/signup, device-authenticated enrollment/sync/reports, health and installer distribution. Installer downloads intentionally remain public for enrollment/update workflows.
- Prisma queries and tagged SQL parameterize inputs; the source sweep found no server raw-unsafe SQL, dynamic command execution or unescaped React HTML insertion. C# raw SQL inspected was fixed migration/pragma text. This is evidence from inspected paths, not proof of absence of every injection flaw.
- Passwords use Argon2; enrollment/device secrets are hashed; Windows device secrets use DPAPI. JWT expiry is enforced, HS256 explicitly selected, and live roles are reloaded. Admin MFA and recovery are not implemented; adding enrollment/recovery UI would change login workflows and is left as a separate owner decision.
- The web app uses explicit Authorization headers, not ambient authentication cookies; classic cookie-based CSRF is not applicable to these routes. Cross-origin API access is not enabled. React escapes rendered values. Bearer tokens remain in sessionStorage to preserve the current session flow; CSP reduces exposure, but a future same-origin XSS could still read them. An HttpOnly-cookie migration would also require CSRF protection and a coordinated session design.
- Installer paths are constrained by fixed directories and validated filenames, and symlink files are rejected. Report downloads require ADMIN and are attachments; report upload is bound to a device/site and a synchronized shift. ZIP validation is not antivirus scanning or complete OpenXML semantic validation.
- Unhandled errors are masked by the existing exception filter. DTO whitelisting rejects unexpected fields; identifiers and bounded quantities are validated at ingress. Unknown-user login and duplicate signup messages do not expose account existence.
- Tracked-source patterns found no private-key/token signatures; ignored real environment files were not read or copied. This does not certify Git history or external deployment secrets. PostgreSQL binds to loopback/private Docker networking, not a public interface.
- NuGet vulnerable-package scan across `SugarERP.slnx`, including transitive packages, reported no vulnerable packages. Desktop runtime/UI flows were not modified; their security review here is a static sweep, not a complete adversarial desktop test.

## Deployment and test boundaries

Apply migrations before starting the updated API; regenerate Prisma Client during the build. Deploy API and web together. The account-security migration is atomic and uses a 15-second lock-acquisition timeout, so a busy database fails the migration safely instead of leaving partially applied schema changes. The initial review used synthetic data only. The owner subsequently authorized merge, push and production deployment; installer publication remains outside this change.

`TRUST_PROXY_HOPS=1` is configured only in production Compose, where the API is bound to loopback and Nginx overwrites `X-Forwarded-For` with the peer address. Do not expose that API directly or reuse the setting behind a different proxy chain. Defaults distrust forwarding headers. Throttling is bounded in-process storage for this single-API deployment; multiple replicas require shared rate-limit storage and process restarts reset counters.

The security integration suite deliberately requires a disposable database named `sugar_security_test`; it tests account removal and audit immutability and must never point at real data. Use `test/compose.security.yml` (tmpfs, loopback port 55439). Example PowerShell:

```powershell
docker compose -f apps/server/test/compose.security.yml down
docker compose -f apps/server/test/compose.security.yml up -d --wait
$env:DATABASE_URL = 'postgresql://sugar_test:synthetic-test-password@127.0.0.1:55439/sugar_security_test?schema=public'
$env:JWT_SECRET = 'synthetic-security-test-key-at-least-32-characters'
npm run prisma:migrate:deploy
npm test
npm run test:integration
docker compose -f apps/server/test/compose.security.yml down
```

Always start the entire integration suite with a fresh container (`down` then `up`); the older contract suite assumes there are no shared catalog events from earlier runs. Reusing the database produced one unrelated feed-count failure; the clean run passed all 19 integration tests.

## Dependency remediation and validation

- npm audit fell from 62 affected package entries to **zero** (8 October 2026), including development dependencies. This is the registry advisory result at review time, not a guarantee against undisclosed vulnerabilities.
- Upgrade NestJS and its CLI/schematics, Vite, Jest and TypeScript ESLint tooling to patched versions. Prisma CLI and Client stay at 6.16.2. Narrow development-tool overrides replace vulnerable Prisma configuration dependencies (`deepmerge-ts` 8.0.2 and `effect` 3.20.0) and the coverage configuration YAML parser (`js-yaml` 4.3.2). Prisma generation, TypeScript checks and unit tests validate the affected paths; the application does not use the changed Map-merging behavior of deepmerge-ts.
- Keep the previously verified, patched Rollup 4.63.1 in the lockfile. An unconstrained lockfile refresh selected 4.64.2 and stalled the admin build with growing CPU/memory use. Restoring 4.63.1 produced the same output bundle and a successful 2.10-second build; the final audit remains zero. Avoid blindly regenerating the lockfile when applying these changes.
- A clean npm installation succeeded. After the final lockfile adjustments, `npm ci --dry-run --ignore-scripts` validated manifest/lockfile consistency. Prisma Client generation succeeded with the configured engine and without bypassing checksums or TLS.
- **46 server unit tests passed** across 11 suites after the final source changes. Server typecheck, server build, admin production build and shared-contract build passed.
- **19 PostgreSQL integration tests passed** across three suites on a fresh disposable database with the final dependency tree on 8 October, including all eight new account-security scenarios. A local Docker engine startup problem was resolved before this final run.
- All 20 migrations applied to an empty database. A separate upgrade fixture applied the first 19 migrations, inserted ACTIVE/PENDING_PERMISSION/DISABLED accounts, and applied the new migration: all statuses/active flags remained unchanged, token versions were zero, and all three identities were backfilled.
- **93 existing .NET tests passed**. The solution-wide NuGet vulnerability scan, including transitive dependencies, found none. No desktop-owned source was edited.
- Browser verification against the local API covered the pending count/filter, explicit approval, exact-username confirmation gating/cancel, disabled self-deletion, and the pending-login error. Permanent deletion/rejection effects and authorization were exercised through HTTP/database integration tests.
- Lint passes for every changed TypeScript file. Full-repository lint still fails with the five pre-existing issues in `src/devices/devices.service.spec.ts` (lines 56, 65, 77) and `src/items/items.service.spec.ts` (line 96, two diagnostics). These unrelated test files were left unchanged. `git diff --check` passes.

Primary dependency references: [deepmerge-ts 8 changes](https://github.com/RebeccaStevens/deepmerge-ts/releases/tag/v8.0.0), [YAML denial-of-service advisory](https://github.com/advisories/GHSA-2883-xcg3-v3hh). See the raw audit output for the full machine-readable result.

## Per-file change inventory

Paths below are relative to the repository root. Existing unrelated working-tree changes were preserved.

| File | Targeted change |
| --- | --- |
| `apps/server/prisma/schema.prisma` | Token version, audit snapshots, and independent historical actor identity; retarget the three business foreign keys. |
| `apps/server/prisma/migrations/20261007010000_user_account_security/migration.sql` | Backfill identities, move foreign keys without changing posted data, revoke tokens on security changes, and make audit records immutable. |
| `apps/server/prisma/bootstrap-admin.ts` | Explicitly activate the bootstrap admin's approval status. |
| `apps/server/src/users/users.controller.ts` | ADMIN-only approve/reject/permanent-delete routes; pass authenticated actor to account mutations. |
| `apps/server/src/users/users.service.ts` | Transactional approval/deletion, dependent credential cleanup, audit insertion, confirmation and lockout protection. |
| `apps/server/src/users/user-security.ts` | Shared administrative lock, live actor recheck, and remaining-admin invariant. |
| `apps/server/src/users/approve-user.dto.ts` | Validate role and optional site IDs for approval. |
| `apps/server/src/users/confirm-user.dto.ts` | Validate the typed deletion confirmation. |
| `apps/server/src/users/create-user.dto.ts` | Normalize names before validation and bound password length. |
| `apps/server/src/users/update-user.dto.ts` | Apply the same name/password validation to edits. |
| `apps/server/src/roles/roles.controller.ts` | Supply actor identity for protected role mutations. |
| `apps/server/src/roles/roles.service.ts` | Prevent ADMIN role removal/rename and administrator lockout. |
| `apps/server/src/auth/auth.service.ts` | Block pending/disabled authentication, generic signup/credential responses, versioned JWTs, and strict device UUID validation. |
| `apps/server/src/auth/jwt.strategy.ts` | Require approved active accounts, current token version, and HS256 on every bearer request. |
| `apps/server/src/auth/auth.controller.ts` | Attach throttling to public login/signup. |
| `apps/server/src/auth/auth.module.ts` | Register the throttle guard. |
| `apps/server/src/auth/auth-rate-limit.guard.ts` | Bounded per-IP login/signup windows using trusted request IPs. |
| `apps/server/src/auth/login.dto.ts` | Normalize username and bound submitted password length. |
| `apps/server/src/auth/signup.dto.ts` | Normalize names and bound submitted password length. |
| `apps/server/src/configure-app.ts` | Trusted-proxy setting, authentication JSON limit, and defensive HTTP headers. |
| `apps/server/src/sync/device-auth.service.ts` | Reject inactive sites, non-writer devices and malformed UUIDs. |
| `apps/server/src/sync/sync.service.ts` | Reject device forgery of server-owned event types; remove a redundant assertion flagged by updated tooling. |
| `apps/server/src/sync/transfer-projection.service.ts` | Replace one assertion with an explicit row return type required by updated lint tooling and TypeScript inference; no runtime change. |
| `apps/server/src/items/items.service.ts` | Remove one redundant type assertion and unused import required by updated tooling; no runtime change. |
| `apps/server/src/reports/xlsx-validation.ts` | Bounded, non-extracting OpenXML ZIP structure validation. |
| `apps/server/src/reports/reports.service.ts` | Use XLSX structure validation and remove a redundant type assertion/import. |
| `apps/server/src/auth/auth.service.spec.ts` | Pending/disabled/unknown login, duplicate signup, JWT approval/version compatibility tests. |
| `apps/server/src/auth/auth-rate-limit.guard.spec.ts` | Quotas, independent buckets, spoofed forwarded headers, route variants and expiry tests. |
| `apps/server/src/sync/device-auth.service.spec.ts` | Site/writer/credential authorization checks. |
| `apps/server/src/sync/sync.service.spec.ts` | Tests rejecting all four server-owned event types. |
| `apps/server/src/reports/reports.service.spec.ts` | Use a real OpenXML ZIP fixture in existing report tests. |
| `apps/server/src/reports/xlsx-validation.spec.ts` | Valid XLSX and malformed/truncated/excessive-expansion cases. |
| `apps/server/test/fixtures/minimal-report.xlsx` | Small synthetic OpenXML fixture. |
| `apps/server/test/users-security.integration-spec.ts` | Full HTTP/database account lifecycle, authorization, immutable history/audit, rollback and concurrent deletion coverage. |
| `apps/server/test/compose.security.yml` | Disposable, loopback-bound tmpfs PostgreSQL test database. |
| `apps/admin-web/src/UserAccountActions.tsx` | Explicit approval role/site selection and exact-username rejection/deletion confirmation. |
| `apps/admin-web/src/App.tsx` | Pending section/count badge, account action wiring, session identity and corrected signup guidance. |
| `apps/admin-web/src/api.ts` | New account endpoints and Arabic security error messages. |
| `apps/admin-web/src/styles.css` | Scoped confirmation form and username wrapping styles. |
| `apps/admin-web/package.json` | Patched Vite version. |
| `apps/server/package.json` | Patched NestJS/build/test tooling; pin Prisma Client to the existing CLI version. |
| `package.json` | Narrow transitive security overrides for development tooling. |
| `package-lock.json` | Reproducible patched dependency resolutions. |
| `deploy/docker-compose.yml` | Configure the single trusted production proxy hop. |
| `deploy/nginx/app.conf` | Overwrite untrusted forwarded IPs and add browser security headers. |
| `apps/server/SECURITY_REVIEW.md` | Findings, compatibility decisions, validation evidence and this inventory. |

Raw audit results and browser screenshots are stored locally under `outputs/admin-security/`; these contain only synthetic test accounts.
