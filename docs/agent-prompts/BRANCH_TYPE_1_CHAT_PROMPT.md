# Implementation agent prompt: Branch Type 1

Specification v1.0 | 8 September 2026

Read this complete file before acting. Module assignment follows, then the full mandatory shared contract. If they conflict, stop the affected work and ask the owner; do not invent a reconciliation.

**First action: verify that `docs/contracts/CONTRACT_V1.md` and the VPS foundation handoff exist. If not, stop and request the Contract v1 checkpoint. Work only in a dedicated Branch Type 1 worktree created from that checkpoint.**

Do not redesign server contracts or central schemas. Generate the C# client/models from the frozen contract where practical, and request changes through an ADR. The application runs natively on Windows; SQLite is embedded and requires no external database installation. Provide reproducible .NET restore/build/test commands and a Windows installer pipeline. Finish with `docs/handoffs/BRANCH_TYPE_1_HANDOFF.md`.

## Your module: Branch Type 1 desktop

Implement the nearby daily-stock retail branch. No cafe/custom-order screen, customer receivables, external pricing or external-invoice creation capability. Share reusable POS, printing, security, sync and shift code with Type 2. Type 1 is a reusable installation profile, not a hardcoded branch called branch1.

### Local schema and boundaries

Migrate SQLite with foundation/cache, one SALEABLE location and hold tracking, stock ledger/balance, shifts/snapshots/counts, requests/shipments/receipts/conflicts, kitchen returns, sales/lines/payments/corrections/refunds, cash movements, audit, outbox/inbox/cursors, report/print jobs. Customer/pricing/recipe ingredient tables are not required locally. Cache only read-only stock summaries of other branches with timestamps. No branch revenue/audit access to another site.

### Implement in this order

1. Installer first-run flow: correct profile, site/device enrollment, authorized catalog download, staff login, printer/export-folder setup. No production sample seeding. Offline first enrollment is unsupported; display why. Resume an open shift safely on restart.
2. Catalog and stock screen: Arabic search, active/inactive history, read-only published prices unless a granted workflow exists, quantity units, current stock, pending incoming holds, last sync. Never allow local manual incoming stock to bypass kitchen confirmation.
3. Morning/evening shift start: one open shift, opening snapshot from the continuous ledger, no forced zero simply because deliveries happen daily. First installation can begin at zero or use an admin-authorized initial stock import. No double addition of yesterday's remaining quantity. Include inactive items that still have stock.
4. Kitchen request screen: searchable products/quantities, draft, submit/outbox status, request history and detail, approved/sent quantities and partial deliveries. Requests do not increase stock. Branch sees kitchen rejection/reductions, cannot edit frozen dispatch quantities.
5. Incoming screen: open downloaded shipment, count each item, confirm matching quantities or report conflict. Transaction posts receipt/ledger/outbox only once. Capture received/count/accept times and shift references. Pending disputed goods are not sellable by default. Show decision pending/site apply state and late-shift supplement.
6. POS: touch item buttons/search, cart with numeric keypad, cash/Visa, table/takeaway, discount/tip validation and decimal math, transactional stock check, original-name/unit/price snapshots and offline-safe receipt number. Save once despite double-tap. Queue print after commit, show persisted receipt and retry/reprint without reselling.
7. Receipt lookup/correction: partial item correction and customer refund by receipt reference, cumulative cap, reason, refund tender and restock/discard decision. Preserve original and link delta/reversal. User-facing effective receipt can update, historical original cannot. Private before/after audit is admin-only. Prior-shift correction posts in active shift.
8. Return-to-kitchen document: choose saleable stock, validate availability, record dispatch timestamp, subtract exactly once, wait for kitchen acknowledgement/dispute. This is مرتجع للمطبخ, distinct from customer refund. At close do not deduct an already-dispatched return again.
9. Four-step close: kitchen return review, physical count, discrepancy review, final summary. Display expected/actual, request approval for corrections; do not silently replace expected with actual. Freeze per-item and cash snapshots plus durable export/print jobs. Closed means closed even if internet/printer/Excel fails. Excel (.xlsx) is the only report format; failed generation retries visibly. Old reports stay unchanged after later resolutions.
10. History: year/month/day/morning-evening/shift navigation, totals and drill-down of sales, effective corrections, incoming receipts, kitchen returns, counts, snapshot and report downloads/reprints. Current-shift access follows cashier permission; broader financial history follows manager/admin role, not the legacy shared password. All list/select controls have search, date filters and paged loading where needed.

### Carryover and after-closing gate

Expected remaining = opening + confirmed incoming + customer restock - retail sold - kitchen returns - waste + approved adjustment. Next opening snapshots current ledger balance; physically counted discrepancy remains separately visible until approved adjustment. Preserve the legacy distinction in migration: old carryover used calculated, not actual. Do not retroactively reinterpret imported shifts.

Legacy after-closing sales exist in the source, so explicitly ask whether to keep them. Default new profile disables them until decided. If retained, post a distinct after-close session/document stream with its own cash/stock events and report supplement, link latest closed shift, require no open shift, prohibit editing original close, and include movements in next opening. Do not subtract twice in both ledger and carryover query.

### Acceptance fixtures

- Opening 5, accepted incoming 20, retail 12, kitchen return 3 => expected 10; next opening 10. Retry incoming and close does not change it.
- Request 20 cakes/4 cheesecakes, kitchen sends 20/2, branch counts 20/2 => accepted incoming 20/2, not requested 20/4.
- Kitchen sends 20, branch counts 18 => held pending admin; later final 18 adds 18 once to the posting shift, original sent 20/count 18 remain visible.
- Sale cake x1 + gateaux x4; correction gateaux -3 restores only those 3 if physically eligible, refunds original allocated amount, retains cake and gateaux x1 and full admin audit. Repeat refund beyond available 1 fails.
- Printer fails, export disk fills, internet disconnects: saved sale/closed shift survives restart; jobs retry without duplicated accounting.
- Type 1 malicious external invoice payload is rejected server-side even if locally crafted.

### Deliver and verify

Provide real migrations from inspected legacy schema, mapping/reconciliation report, no-destructive-recovery startup, testable use cases and headless Arabic UI tests, Windows printer manual checklist, clean-VM installer install/update/uninstall evidence and sync simulator tests. Installer carries profile, never a fixed branch ID/secret/database. Deliver operator instructions for requests, holds, refunds, shift close, failed export, offline status and recovery. Repository/push/deploy gates from shared contract apply.

---

# Mandatory shared system contract - Sugar ERP

Specification version: 1.0 | 8 September 2026 | Proposed implementation contract, not a claim of delivered software.

## Read this first

You are implementing one module of a four-module pastry-business ERP. Read your entire module prompt before coding. Keep this shared contract consistent across all four modules. Build and test locally first; do not deploy merely because a deployment script exists.

The VPS/foundation chat asks once for the project/repository name and GitHub owner. Desktop chats use that confirmed repository and must not repeat this question. Do not create a remote or push until explicitly authorized. Inspect the provided repository and its AGENTS.md before changing it. Do not create a remote, push, rewrite history, or deploy until the user confirms the target and grants the corresponding action. Never force-push. This question belongs to the implementation agent; this specification does not create a repository.

Use a modular monolith on the VPS, shared desktop domain/application/infrastructure libraries, and three desktop application profiles. Four modules do not imply four microservices or four repositories. Proposed monorepo: `src/Domain`, `src/Application`, `src/Infrastructure.Local`, `src/Contracts`, `src/Sync.Client`, `src/Desktop.Shared`, `apps/Branch1`, `apps/Branch2`, `apps/Kitchen`, `apps/Server`, `apps/AdminWeb`, `tests`, `deploy`, `installers`, `docs`. Ask before changing an established layout. Use C#/Avalonia with Entity Framework Core and SQLite for native Windows desktop applications, and TypeScript/NestJS with PostgreSQL for the central VPS backend. The VPS stack and local server development use Docker Compose. The Admin frontend framework remains pending owner approval. Verify supported framework/package versions and compatibility from official documentation at implementation time, pin them and commit lockfiles. Do not blindly reuse version numbers in the legacy document.

## Source precedence and traceability

1. Explicit user clarifications during implementation.
2. Latest visible conversation: VPS central system; two reusable branch TYPES; every desktop including kitchen installed from VPS-hosted installers; Type 1 has no special/cafe orders.
3. `The architecture.docx`: desired business workflows, Type 2 freezer/display, cafe credit, kitchen ingredients and variance.
4. `SYSTEM_ARCHITECTURE_AND_MODULES.md`: legacy implementation reference, dated 26 August 2026, not proof that this new ERP exists.

The Word file says both branches can issue cafe orders. That conflicts with the latest conversation: disable this capability for Type 1. Branch type is a feature profile, not a branch identity. Many branches may use the same type. The legacy document has manual incoming receipts, hard deletion of incoming lines, a shared history password, plaintext recovery answers, after-closing sales, and packaging that can copy Data into builds. These are not acceptable defaults for the new system. Preserve useful behavior: Arabic RTL, touchscreen UI, cash/Visa, table/takeaway, discounts/tips, decimal rounding, historical snapshots, shift carryover, receipt reprint, Excel (.xlsx), Windows raster printing, and layered tests. Do not silently remove those features.

## Fixed scope versus pending decisions

Fixed: admin sees all branches' revenue and stock, kitchen inventory, outstanding cafe balances, conflicts, corrections and sync health. Branches see both branches' stock as read-only last-synced projections, not their revenue or private audit trails. Type 1 has one saleable location. Type 2 receives into freezer and transfers to/from display; retail uses display, cafe orders and kitchen returns use freezer. Kitchen and Type 2 have cafe orders; cafe goods are not returnable through the normal workflow. Kitchen deductions are triggered by dispatched output using a recipe snapshot. Only admin resolves quantity disputes. Excel is a financial handoff, not the sync protocol.

Confirm these before their affected production feature: disputed goods saleability (default HOLD, not saleable); full versus partial receipt acceptance (default hold the whole disputed shipment); recipe deduction at dispatch versus separately tracked production (default dispatch only, never both); whether initial freezer issue must be accepted online (default sync required to learn new shipment); who can correct sales and price lists; physical count adjustment approval; after-closing behavior (default disabled until approved); refund treatment of tips and original order discount; branch/customer names, timezone, printer hardware, units/precision, backup retention and recovery objectives. Default Egypt timezone is Africa/Cairo, configurable. Default currency EGP. These are design proposals, not invented confirmed requirements. Keep a decisions log and do not describe unresolved policies as implemented business agreement.

## Ownership and availability

VPS owns branch/device enrollment, permissions, catalog/prices/recipes, customer identity, final dispute decisions, and consolidated read models. Each site's single enrolled writer owns its operational sales, shifts, counts, dispatches and stock ledger. VPS validates and mirrors these immutable events; it must not create a second deduction while ingesting them. Admin corrective commands are queued to the owning site's writer and are effective only after that writer applies and acknowledges them. Dashboard shows decision-created versus applied state separately. Never overwrite a desktop stock balance from a stale server snapshot.

Start with exactly one active stock-writing device per site. Additional tills require a site coordinator or another explicitly designed concurrency model; cloning a database is not multi-till support. Local operations work offline after initial enrollment and cached authorization. New cross-site information and admin resolutions require communication. A branch can verify an already-downloaded signed/versioned shipment while offline; a request still only in a branch outbox has not reached the kitchen. The admin and other branches see last synchronized stock with `as_of` and stale/offline indicators, never a false real-time promise. A VPS outage delays coordination and uploads, not existing local sales.

## Identity, precision, and invariants

- Generate UUIDs locally and keep the SAME IDs on server; do not maintain unrelated local/server IDs. Include site_id, device_id, actor_id on owned events. Foreign keys retain UUID identity.
- Store UTC occurrence and server receipt timestamps, local business_date, timezone identifier, monotonic device sequence and stream epoch. Device clock changes must not reorder synchronization. Warn on suspicious clocks; do not rewrite historical timestamps.
- Money: signed integer minor units (EGP piastres), calculated with decimal, two-place midpoint-away-from-zero rounding. Quantity: signed scaled integer in an item's fixed base unit, with quantity_scale. Piece items scale 1; kilogram scale 1000 by default. Recipe ratios use exact decimal/rational calculations with explicit rounding. Never SQL REAL/double for stock or currency. Lock units/scale after first use.
- Domain rows use explicit FKs, NOT NULL where required, enum/check constraints, positive line quantity and nonnegative posted balances. No cascade deletion of posted documents. Inactive items remain visible when stock remains; selling them requires a deliberate policy, not disappearing stock.
- One open shift per site with a partial unique index; unique `(site_id, device_id, stream_epoch, business_date, document_type, sequence)` for offline-safe printed numbers. UUID is the true identity, not a receipt number alone.
- A stock-changing operation commits header, lines, ledger movements, balance projection, sequence, audit, and sync outbox in ONE local transaction. Read and validate stock under the write lock. Printing, HTTP and exports happen after commit through retryable jobs.
- SQLite: foreign keys ON on every connection; WAL, busy timeout, durable writes, short serialized writes. PostgreSQL: explicit transaction, deterministic row-lock order, unique constraints and retry handling for serialization/deadlock failures. Do not rely on UI checks alone.

## Canonical data model

Notation: `id` means UUID PK; references named `*_id` are FKs unless described as external source keys. `?` means nullable. `q` means scaled integer; `m` means integer minor units; `ts` means UTC timestamp. Owned transactional headers also carry site_id, device_id, actor_id, occurred_at, business_date, version, command_id. Lines snapshot item name, unit, scale, quantity, and price where monetary. This is a logical schema; deliver runnable dialect-specific migrations, indexes, check constraints and typed repository mappings, not JSON blobs in place of relational facts.

### Foundation and configuration

| Table | Essential columns and rules | Placement |
|---|---|---|
| sites | id, code UNIQUE, name, type BRANCH1/BRANCH2/KITCHEN, timezone, active | VPS; local identity and read-only directory |
| devices | id, site_id, profile, enrollment_status, key_thumbprint, active_writer, last_seen_at, app_version, stream_epoch | VPS; own local identity |
| users / user_site_roles | user id, login UNIQUE, password_hash; user_id + site_id + role UNIQUE | VPS; minimal scoped offline identity cache |
| offline_grants | id, user_id, device_id, permissions, issued_at, expires_at, signature, revoked_version | local verified cache |
| items | id, sku UNIQUE, name_ar, unit, quantity_scale, kind PRODUCT/INGREDIENT, active, version | VPS; scoped cache |
| retail_prices | id, site_id, item_id, price_m, effective_at, version; unique effective revision | VPS; site cache |
| locations | id, site_id, code, kind; UNIQUE site+code | VPS definition; local cache |
| settings / schema_migrations | typed key/value config; migration version, checksum, applied_at | both |

### Stock and shifts (all operational sites)

| Table | Essential columns and rules |
|---|---|
| stock_documents | id, kind, status, shift_id?, external_reference?, reversal_of_id?, reason; typed child documents reference this header |
| stock_movements | id, document_id, line_id, item_id, location_id, delta_q, posting_shift_id?, occurred_at; UNIQUE source line + movement leg |
| stock_balances | site_id + location_id + item_id PK, quantity_q, revision; rebuildable projection, not independent truth |
| stock_transfers / stock_transfer_lines | id, stock_document_id UNIQUE, from_location_id, to_location_id, shift_id, transferred_at; line id, transfer_id, item_id, quantity_q; source != destination |
| stock_holds | id, receipt_line_id or return_receipt_line_id, location_id, physical_count_q, status, released_q, disposition, decision_id?; non-saleable custody, never included in available balance |
| shifts | id, kind MORNING/EVENING, opened_at, closed_at?, status, opening_cash_m, expected_cash_m?, actual_cash_m?, difference_m? |
| shift_item_snapshots | shift_id + item_id + location_id PK; opening_q, incoming_q, sold_q, customer_return_q, kitchen_return_q, external_q, transfer_in_q, transfer_out_q, waste_q, adjustment_q, expected_close_q, actual_q?, discrepancy_q?, name/unit snapshots |
| stock_counts / stock_count_lines | count id, shift_id?, status, counted_at; count_id + item_id + location_id, expected_q, actual_q, difference_q |
| adjustment_requests | id, count_line_id?, reason, proposed_delta_q, status, admin_decision_id?, applied_document_id? |
| cash_movements | id, shift_id, kind OPENING/SALE/REFUND/CUSTOMER_PAYMENT/PAY_IN/PAY_OUT, amount_m, source_id, reason |

Ledger is append-only. A reversal is another signed movement linked to the original; never edit quantity history. Transfers have two equal/opposite legs for the same item in one transaction. Initial balance is a one-time authorized ledger import, not repeatedly added on each shift. Opening and closing snapshots describe stock; they do NOT add/subtract it. Cached totals must reconcile to events. Record physical counts separately from expected stock; no automatic silent count adjustment. Hold rows represent custody before posting confirmed incoming; releasing a hold must not also count it as a second incoming movement. Enforce exactly one source FK for alternative source columns (receipt OR return receipt, conflict OR count adjustment), not an unvalidated polymorphic reference. Add indexes on every line parent FK and on site + business_date, item + location + occurred_at, shipment status + destination, customer + issue date, and outbox state + next_attempt_at.

### Inter-site requests, shipments, receipts and returns

| Table | Essential columns and rules |
|---|---|
| kitchen_requests / kitchen_request_lines | id, requesting_site_id, requested_at, status; line id, request_id, item_id, requested_q, approved_q? |
| shipments / shipment_lines | id, request_id, source_site_id, destination_site_id, dispatched_at?, status, version; line id, shipment_id, request_line_id, item_id, sent_q, recipe_version_id? |
| incoming_receipts / incoming_receipt_lines | id, shipment_id UNIQUE, receiving_shift_id, counted_at, accepted_at?, status; line id, receipt_id, shipment_line_id UNIQUE, sent_q, counted_q, confirmed_q? |
| quantity_conflicts / conflict_lines | id, receipt_id or return_receipt_id, status, reported_at; line id, conflict_id, source_line_id, sent_q, counted_q, note |
| admin_decisions | id, conflict_id or adjustment_request_id, admin_id, expected_version, decided_at, reason, application_status |
| decision_lines | decision_id + source_line_id PK, final_q, disposition of shortage/overage, resulting_command_id |
| kitchen_returns / kitchen_return_lines | id, source_site_id, destination_kitchen_id, shift_id, dispatched_at, status; line id, return_id, item_id, sent_q |
| return_receipts / return_receipt_lines | id, return_id UNIQUE, kitchen_received_at, status; line id, return_receipt_id, return_line_id, counted_q, confirmed_q?, disposition HOLD/RESTOCK/DISCARD |

Keep request and shipment separate: requested 20 can become sent 15. Support multiple shipments per request using cumulative quantity validation and explicit fulfillment completion. Receiving a shipment cannot create another request. Kitchen may reduce approved quantities or reject; adding unrelated items or exceeding requested quantities needs branch-approved revised request. Type 1 cannot fabricate manual incoming documents. Partial shipment means multiple dispatch documents, not quietly changing sent history.

Request state: DRAFT -> SUBMITTED -> APPROVED or REJECTED; aggregate fulfillment PARTIAL -> FULFILLED/CLOSED. Shipment state: DRAFT -> DISPATCHED -> AWAITING_RECEIPT -> RECEIVED or CONFLICT -> RESOLVED. Accepted/dispatch quantities freeze after posting. Retries cannot redispatch or rereceive the same document.

On dispatch kitchen posts ingredient consumption once and records product quantity in the shipment transit projection. Branch counts against the sent version. If matched, one receipt posts confirmed incoming stock to SALEABLE (Type 1) or FREEZER (Type 2). If disputed, record physical count and hold it outside saleable inventory; whole shipment held by default pending policy confirmation. Admin chooses final quantity with reason and shortage/overage disposition. Site applies the resolution once to release the hold and post confirmed stock; unexplained count differences remain explicit variance, not invented goods. Do not deduct kitchen ingredients again on receipt or conflict resolution. Returning products removes branch stock at dispatch, tracks transit, and kitchen acknowledges count/disposition; do not reverse raw ingredients because finished cakes came back.

If resolution arrives after shift close, post in the current open shift (or wait until one opens); keep original receiving shift/time as references. Do not rewrite the closed snapshot or original exported file. UI/report has a separate later-resolution section. Closed shifts with held shipments must explicitly disclose pending quantities.

### Retail sale and corrections (branch profiles)

| Table | Essential columns and rules |
|---|---|
| sales | id, shift_id, receipt_number, payment_method CASH/VISA, fulfillment TABLE/TAKEAWAY, subtotal_m, discount_m, tip_m, total_m, status |
| sale_lines | id, sale_id, item_id, quantity_q, unit_price_m, allocated_discount_m, total_m, name/unit snapshots |
| sale_payments | id, sale_id, method, amount_m, paid_at, reference?; posted total equals sale total |
| sale_corrections / correction_lines | id, original_sale_id, shift_id, kind VOID/REFUND/CORRECTION, reason, actor, occurred_at; original_line_id, quantity_delta_q, amount_delta_m, restock_q, disposition |
| refund_payments | id, correction_id, method, amount_m, paid_at, reference? |

Employee opens original receipt, changes a line through a correction workflow, and provides reason. Persist the original plus linked reversal/replacement or correction delta; never UPDATE the original line to erase evidence. Admin-only audit view shows before/after, actor, timestamps and reasons. Operational staff can see effective receipt quantities and prior refunds needed to prevent duplicate refunds, without the private audit feed. Local files are not protected against a hostile Windows administrator; document this boundary.

Returning 3 out of 4 gateaux is not returning the cake on the same receipt. Cumulative corrected/refunded quantity cannot exceed original net eligible quantity; lock original line during correction. Financial refund and physical restocking are separate. Returned damaged food does not become saleable; incorrect entry reversal can restore stock because goods never left. Allocate original discounts proportionately with a deterministic remainder rule; refund original effective price, not today's catalog price. Tip policy must be confirmed. A prior-shift refund posts cash/stock effects to the current shift and links the original without altering historical closing totals. Visa is a recorded tender unless a payment integration is explicitly commissioned; do not pretend a terminal refund occurred.

### Cafe credit and pricing (Type 2 and kitchen only)

| Table | Essential columns and rules |
|---|---|
| customers | id, name, contact?, active |
| price_lists / price_list_versions / price_list_items | list id, name; version id, list_id, effective_at; version_id + item_id PK, price_m |
| customer_price_lists / customer_price_overrides | customer_id + effective_at, list_version_id; customer_id + item_id + effective_at, override_price_m |
| external_invoices / external_invoice_lines | id, customer_id, issuing_site_id, shift_id, invoice_number, issued_at, due_at?, currency, total_m, price_version_id, status; line id, invoice_id, item_id, quantity_q, unit_price_m, total_m, snapshots |
| customer_payments / payment_allocations | payment id, customer_id, collecting_site_id, shift_id?, amount_m, method, paid_at, reference; payment_id + invoice_id PK, allocated_m |

A cafe may reuse another cafe's base list; changing one cafe independently creates an override (or an explicit cloned list), not a silent shared-list mutation. Existing invoices never change with prices. Pricing authority is VPS; default edits online and role-authorized; offline invoicing uses cached published prices with revision recorded. Catalog refresh during checkout must not silently change a confirmed cart: warn and reconfirm the new total.

Issued external invoice reduces stock exactly once and creates receivable; it also queues a print job and retains a digital invoice. Printing failure must be visible and retryable, not cancel/reissue the invoice. No normal cafe returns. Administrative error correction requires an audited reversal process and an explicit business decision, not a disguised customer return button.

The Paid action records an actual payment plus allocation, it is not a boolean toggle. Support partial payments and a customer balance aggregated across issuing sites. Never double-count a payment as new sales. Propose online-only cross-site payment allocation to prevent double settlement: an offline collection may be stored as an unallocated pending payment, with no final paid claim until server allocation. Lock invoice balances for allocation; enforce allocated <= outstanding and <= payment amount. Mistaken payments use reversals; never delete cash history.

### Kitchen ingredients and cost

| Table | Essential columns and rules |
|---|---|
| recipes / recipe_versions / recipe_lines | recipe id, product_item_id; version id, recipe_id, effective_at, yield_q; version_id + ingredient_item_id, ingredient_q, unit conversion |
| ingredient_receipts / ingredient_receipt_lines | receipt id, kitchen_site_id, received_at, supplier_reference?, total_cost_m; line id, receipt_id, ingredient_item_id, quantity_q, unit_cost_m |
| consumption_batches / consumption_lines | batch id, dispatch_id UNIQUE, recipe mode, occurred_at; line id, batch_id, ingredient_item_id, expected_q, recipe_version_id, unit_cost_snapshot_m |
| inventory_thresholds | site_id + ingredient_item_id PK, low_q, recovery_q, enabled |
| stock_variances | id, count_line_id, period, expected_q, actual_q, unexplained_delta_q, cost_snapshot_m, reason, approval_status |
| alerts | id, dedupe_key, kind, entity_id, opened_at, acknowledged_at?, resolved_at?, severity |

Dispatch-derived recipe consumption is the user's requested baseline: 20 cakes x 5 eggs = 100 eggs on dispatch, once. It estimates consumption at output time, not actual production time. External kitchen dispatch uses the SAME consumption path. Recipe versions/yields and units freeze in consumption lines. Log direct kitchen waste and physical count differences separately. Do not implement a second production deduction; a future production mode must convert ingredients into tracked finished goods and then dispatch only finished goods, with a migration and explicit approval.

Monthly expected ingredient closing = opening + receipts - recipe consumption - recorded waste + authorized adjustments. Unexplained usage = expected - physically counted before its adjustment. Example: expected eggs 700, actual 200 => unexplained shortage 500 eggs, not necessarily proven waste. Report recorded waste and unexplained shortage separately with valuation basis, never claim profit from revenue alone. Proposed weighted-average ingredient cost; confirm accounting policy. Actual exhausted stock must be recorded, alerted and reviewed; default block negative stock, with no silent stock creation to let a dispatch proceed.

### Synchronization, audit and side effects

| Table | Essential columns and rules |
|---|---|
| sync_outbox | event_id PK, device_id, stream_epoch, sequence UNIQUE within stream, aggregate_id, event_type, payload_version, payload_json, payload_hash, created_at, attempts, next_attempt_at, state, acknowledged_at? |
| sync_inbox / processed_commands | event_id or command_id PK, payload_hash, applied_at, result_version; dedup incoming changes |
| sync_cursors | feed_scope PK, cursor, snapshot_version?, updated_at |
| server_event_log | server_cursor PK, event_id UNIQUE, site_id, aggregate_id, payload, ingested_at; permission-filtered feed |
| audit_events | id, actor_id, site_id, device_id, action, target_type/id, before_json?, after_json?, reason, occurred_at, ingested_at; append-only |
| side_effect_jobs | id, source_id, kind PRINT/EXPORT/UPLOAD, document_version, state, attempts, last_error, next_attempt_at |
| report_artifacts | id, shift_id, report_version, content_hash, format, file_key/local_path, generated_at, uploaded_at?, error? |
| releases | id, profile, version, channel, platform, artifact_key, sha256, signature, min_api_version, min_schema_version, published_at |

Do not send complete SQLite files or mirror arbitrary tables over HTTP. Define versioned domain contracts and server-authorized commands. Event envelope: event_id, site_id, device_id, stream_epoch, device_sequence, actor_id, aggregate_id, aggregate_version, event_type, payload_version, occurred_at_utc, business_date, payload, payload_hash. Server must derive/check site/device authorization from credentials, not trust those fields.

`POST /api/v1/sync/push`: bounded events ordered by device sequence, individual responses ACCEPTED/DUPLICATE/RETRYABLE/REJECTED, durable server cursor per accepted event. Duplicate same ID/hash returns prior result; same ID/different hash is integrity failure. Device acknowledges only after durable server response; server saves event, projection and dedup record together. Unresolved sequence/dependency gap returns retryable dependency status. Do not skip a rejected stock event and apply dependent later stock events; isolate blocked aggregate/stream and surface repair work. Never silently drop the outbox.

`GET /api/v1/sync/pull?cursor=...`: ordered permission-scoped change feed with opaque cursor, page size, has_more and compatibility metadata. Apply each page plus cursor in one local transaction with inbox dedup; skip echo business effects already committed locally while still acknowledging their server status. Server materializes a stable snapshot at cursor C for enrollment/recovery, then sends deltas after C. Retention expiry triggers controlled snapshot recovery, not deletion of unsent local data. Backoff with jitter, bounded batches, timeouts and cancellation; status screen shows pending count, oldest age, last success and actionable permanent failures. WebSockets are optional wakeups, not a replacement for durable polling.

Administrative commands use expected_version; stale edits return 409 with current state. Record decision and command on VPS, local apply with stock validation, then applied acknowledgement. Site handles revision conflicts explicitly; impossible negative corrections remain blocked for reconciliation. One device owns the local transaction, so two nodes never both apply the same inventory effect. Restore of an old local backup requires a new authorized stream epoch, recovery of acknowledged events and reconciliation of outbox before resuming sales. Do not roll a local database back across newly accepted sales just to downgrade the executable.

## Shift math, reporting and examples

Remaining = opening + confirmed incoming + customer restock + transfer in - retail sold - cafe dispatched - kitchen returns - transfer out - waste + approved adjustment. Count discrepancy = actual - expected. Customer refund without restock affects money, not this stock formula. Do not subtract customer returns as if they were kitchen returns.

Type 1 example: opening 5 + incoming 20 - sold 12 - kitchen return 3 = remaining 10; next shift opening 10 unless a separately posted adjustment occurs. Type 2: freezer 100; move 30 to display => freezer 70/display 30; retail 20 => display 10; cafe 15 => freezer 55; return display 10 at close => freezer 65/display 0. Internal transfers never create revenue or change total site quantity. Snapshot display before and after close transfer so the report explains the 10 unsold pieces rather than showing only a misleading zero.

Close transaction freezes snapshots and cash summary; it does not wait for internet, printer or Excel. Generate durable retry jobs in that transaction. Excel (.xlsx) is the only report export and upload format. Do not implement CSV generation, upload, download, or fallback. Unlike legacy best-effort XLSX, failed Excel generation must remain visible and retryable. Arabic labels include افتتاحية، وارد، مباع، مرتجع للمطبخ، متبقي; extend with customer restock, cafe issue, transfer and waste columns where applicable. Upload finalized bytes + SHA-256 + shift/report version; idempotent hash key, safe filenames, size/type checks and authorized download. Old exports are immutable; corrections produce supplement/version with links, not overwrite.

Admin revenue: retail net sales after discounts/refunds by originating site, cafe invoiced net sales separately by issuing site/customer, payments collected by collecting site/date, receivables outstanding, tips separately. Group history year -> month -> business day -> morning/evening -> shift -> original receipt and amendments. Do not add invoiced amount and payment receipts as revenue twice. Internal kitchen shipments are stock movements, not external revenue. Dashboard must distinguish cash collected from sales, and sales from profit. Month and business day use configured site timezone; source sale dates and correction posting dates are both retained.

## Security, installers, operations and delivery gates

Use named users, server-side role/site/profile authorization, strong password hashing, rate limiting, session expiry, admin MFA and secure recovery. No shared four-character history password or plaintext recovery answer. Desktop offline grant is device-bound, expiring and signed; store secrets with Windows protected storage and restrict database/backup ACLs. Offline revocation takes effect on reconnect or grant expiry, not instantly. No PostgreSQL port or credentials on desktops. HTTPS only, secure admin cookies, CSRF controls when using cookies, output encoding, least-privilege service/database roles, audit-log redaction and dependency/secret scans. Admin-only audit is application authorization, not a promise that plaintext SQLite resists a machine administrator. Confirm offline-grant TTL before production.

VPS topology: TLS reverse proxy -> admin web/static assets and API -> PostgreSQL; API/worker also use durable volumes for shift uploads and installer artifacts. No public database port. Background worker can share codebase but runs separately for durable jobs. Docker Compose is the required local-development and single-VPS deployment baseline; Kubernetes, brokers and distributed databases are out of scope unless measured need exists. CI/CD uses GitHub Actions; Windows runner builds Windows installers, Linux runner builds/tests server. The VPS hosts releases and deployment runtime, not necessarily the compilation runners.

Produce separate signed Windows installer artifacts for Branch Type 1, Branch Type 2, Kitchen from shared code. Self-contained publish includes managed runtime but native prerequisites still require clean-VM testing. Choose and document installer/updater tooling after validating licensing/support. No per-branch source forks. Admin creates site and selects type, issues one-time short-lived enrollment code; installer downloads from same VPS via HTTPS, first run enrolls correct profile/device, validates TLS, selects printer/export folder and downloads authorized catalog. Installer contains no admin password, device secret, live database or real customer data. Build checks must fail if Data, exports, backups, keys or credentials enter packages.

Install binaries in appropriate Windows application directory and operational data in a stable writable protected data directory, never inside versioned binaries. Upgrade preserves database, exports, settings and enrollment. Do not switch an existing enrolled site's type locally. Update manifest includes version, profile, architecture, checksum, signature, compatibility and release channel. Verify signature and hash before install; TLS/hash alone is not publisher authenticity. Pause updates during open shift, pending critical transfer apply, or migration lock; back up consistently, migrate once, health-check. Roll back binaries only if schema compatible; never restore an old DB over new business transactions. Uninstall preserves data by default; destructive removal requires explicit owner confirmation.

CI on pull request: restore pinned dependencies, format/lint, unit tests, SQLite/PostgreSQL integration, contract tests, migration upgrade from sanitized legacy fixtures, Arabic headless UI, web authorization tests, secret/dependency checks. Release: immutable tag/commit, reproducible outputs where feasible, SBOM, signatures, staged deployment, backup, expand-compatible migrations, health/smoke tests, owner-approved promotion, installer publication. Secrets come from approved CI/VPS configuration, never commits or screenshots. Deployment automation should have least privilege and an environment approval gate.

Proposed recovery targets for owner approval: server RPO 1 hour/RTO 4 hours; local machine daily and pre-upgrade backups, recognizing unsynced local events are at risk until backed up or uploaded. Use PostgreSQL base backups/WAL strategy appropriate to approved RPO; encrypted off-VPS copies, retention policy and monthly restore exercise. SQLite live backups use supported online backup API, not copying only the .db under WAL. Back up installer keys separately and securely. Same-VPS backups alone do not cover VPS loss. Metrics: outbox age/depth, last device heartbeat, sync rejection, unresolved conflict age, disk usage, backup freshness, database health, failed reports/print jobs; redact private financial data from generic logs.

## Implementation sequence and non-negotiable verification

1. Ask project/repo name and target; inspect sources, existing code and data risk; document confirmed versus proposed requirements. Work on an isolated branch; no live data modifications.
2. Freeze shared IDs, quantities/money, ownership, state machines, API/OpenAPI/event JSON schemas and a small cross-module fixture. Implement foundation/migrations first; no four incompatible schemas.
3. Build one vertical slice: branch request -> kitchen dispatch -> receipt -> sale -> close -> VPS view; include an offline retry and conflict resolution before broad UI work.
4. Complete module screens, history, security, printing/exports and failure recovery. Type 2 reuses tested Type 1 POS behavior with location policy, not copy-pasted divergent accounting.
5. Run full integration and recovery test matrix; document limitations with evidence. Produce installers/deployment files and run clean Windows VM/staging tests. Publish/push only to the user-approved target and scope.

Required test matrix: replay identical event 10 times; crash after local commit before send; server commit with lost response; crash during pull before cursor commit; sequence gap; unauthorized other-site payload; expired/revoked grants; duplicate admin decision; simultaneous last-item sale attempts; offline catalog revision; partial refund with original discount; old-shift correction; hold receipt resolved in later shift; freezer/display transfer conservation; same cafe paid from two sites; recipe version changes; repeated dispatch without duplicate eggs; reported physical shortage; failed printer; disk-full export; corrupt upload hash; migration interruption; installer excludes live Data; update preserves unsynced outbox; backup restore and event replay; clock jump and midnight/month boundary.

Proposed performance budgets to measure on an agreed shop PC: local sale commit p95 <=300 ms excluding printing; item search p95 <=150 ms for 2,000 items; initial paged history p95 <=1 s for 100,000 receipts. Do not claim these are measured. Verify screen sizes 1280x720 and 1920x1080, high DPI, touch-only use, Arabic shaping, numeric entry and actual receipt printer. Search all entity selectors and histories, preserve hidden count rows under filters, prevent duplicate taps, and provide in-app keypad where needed.

Deliver source, migrations, seeded SYNTHETIC demo, OpenAPI/event schemas, automated tests and actual test results, installer scripts/artifacts, CI/deploy definitions, environment example, backup/restore/upgrade/operator runbooks, architecture decisions, migration reconciliation report and explicit unfinished items. A mock UI, empty repository methods or untested deployment script is not production completion. If hardware/network/credentials are unavailable, state the exact unverified gate and do not claim production readiness.

## Technical references for implementation checks

- Legacy behavior source: `SYSTEM_ARCHITECTURE_AND_MODULES.md`, sections 6, 8, 12-17, 20-23. Existing schema and behavior must be verified against actual repository before migration.
- Business source: `The architecture.docx`, branch1 workflow, cafe pricing/payment paragraphs, branch2 freezer/display paragraph, kitchen ingredients/variance paragraphs.
- SQLite online backup: https://sqlite.org/backup.html - use a consistent backup mechanism for active databases.
- SQLite WAL: https://www.sqlite.org/wal.html - account for WAL state when backing up.
- PostgreSQL locking: https://www.postgresql.org/docs/current/explicit-locking.html - validate concurrent mutations transactionally.
- .NET deployment: https://learn.microsoft.com/en-us/dotnet/core/deploying/ - self-contained deployment is platform-specific; test native prerequisites.

These references support technical mechanisms, not proof that the proposed ERP is already built, secure or accounting-compliant. Recheck supported versions at implementation time.
