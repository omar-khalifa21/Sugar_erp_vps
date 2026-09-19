# Sugar Branch Type 1 → VPS handoff

This is a working handoff, not a claim of production readiness. Branch desktop worktree: `C:\sugar-branch-type-1`; central repository/VPS-owned code: `C:\sugar`. Read `AGENTS.md`, `docs/agent-prompts/SHARED_PROJECT_CONTEXT.md`, `docs/agent-prompts/VPS_CHAT_PROMPT.md`, and `docs/contracts/CONTRACT_V1.md` before VPS edits. The frozen contract is authoritative; changes require an ADR/coordinator approval. Do not use local Docker for this user's workflow; Docker is intended for the VPS. Do not push or deploy without explicit authorization.

## User priorities

- Branch app must be fast, clear, Arabic RTL, keyboard navigable, and reliable in production.
- Retail and cafe/custom-order revenue must remain separate; read-only VPS/admin views should show site-scoped data, outstanding customer balances, stock, shifts, and sync freshness. Do not double-count payments as revenue.
- Branch data must reach the VPS automatically; kitchen must receive actionable requests there. No duplicate sales, shipments, payments, or stock movements.
- No licence screen. The former blocking device-enrollment screen was removed; secure one-time VPS device linking remains in Settings. It is not a paid licence. Without a real HTTPS `/api/v1` address and admin-issued token the branch cannot operate/sync; `--demo` uses isolated local data and never uploads.
- User does not want local Docker. Receipt printer needs actual physical verification; passing bitmap/unit tests is not proof of paper output.

## Branch implementation verified in code

- Desktop is C#/Avalonia + EF Core SQLite. Local transactions queue versioned immutable `OutboxMessage` records; `InboxMessage` deduplicates incoming events. Money is integer minor units; stock is scaled integers.
- `BranchSyncService` uploads ordered batches (max 100) via `POST /api/v1/sync/push`, then pulls pages via `GET /api/v1/sync/pull`, applies each page/cursor transactionally, and acknowledges via `POST /api/v1/sync/ack`. Device auth headers are `X-Device-Id` and `X-Device-Secret`; HTTPS required outside loopback. Enrollment is `POST /api/v1/enrollment`.
- Background loop was added to the Branch Type 1 app; it runs every 15 seconds in non-demo mode, with cancellation on exit. A semaphore serializes background and manual full sync. Existing retry/backoff and sequence ordering remain in the sync service. Manual sync is still available.
- Outgoing event types currently include `shift.opened`, `shift.closed`, `sale.completed`, `sale.corrected`, `catalog.item.updated/deleted`, `kitchen_request.submitted`, `incoming_receipt.accepted/disputed`, `kitchen_return.dispatched`, `cafe_customer.created/price_list_updated`, `custom_order.created/status_changed`, and `custom_customer.payment_recorded`.
- Incoming handlers currently apply `catalog.item_published/updated`, `shipment.dispatched`, `kitchen_request.updated/approved/rejected`, `quantity_conflict.decided`, `kitchen_return.acknowledged`, `stock.projection_published`, and `device.bootstrap`. Unknown incoming types are recorded in the inbox without a business projection; assess whether that is appropriate.
- Important user override: custom orders were added to Branch Type 1 despite the older project invariant that Type 1 has none. Resolve this contract/profile conflict explicitly before VPS authorization is finalized; do not silently reject those events or silently widen every Type 1 device's permissions.

## VPS gap to address next

The current VPS NestJS sync service validates credential/epoch/hash/sequence/dependencies and stores immutable `syncEvent` rows; its pull returns same-site event rows. In code inspected on 2026-09-12, no business projection consumers or read-only sales/orders/stock endpoints were found. Thus an accepted event is **not yet** a usable kitchen/admin record. Build idempotent transactional projections and authorized read-only APIs (with `as_of`/staleness), plus kitchen routing for branch requests and cross-site shipment/decision feeds. In particular, same-site-only pull is insufficient for Branch → Kitchen workflow. Preserve original UUIDs and never apply a second stock deduction centrally.

Do not claim live synchronization, dashboard visibility, or printer success until a real enrolled device, VPS, kitchen flow, and physical printer have been end-to-end tested. No real VPS address/token was provided in this thread.

## Recent verification

On 2026-09-12, `dotnet test SugarERP.slnx --configuration Release --no-restore` passed 35/35 tests and Branch Type 1 Release built with zero warnings/errors. The app was relaunched from `C:\sugar-branch-type-1\apps\branch-type-1\bin\Release\net9.0\SugarERP.Branch1.exe`. These checks do not verify the VPS integration or paper printout.
