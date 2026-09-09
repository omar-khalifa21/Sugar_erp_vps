# Final integration chat prompt

You are the integration owner for Sugar ERP. Work from a clean integration worktree created from the approved base branch.

Read the root `AGENTS.md`, `docs/agent-prompts/SHARED_PROJECT_CONTEXT.md`, `docs/contracts/CONTRACT_V1.md`, all accepted ADRs, and all four module handoff reports.

Do not redesign working modules. Integrate the VPS/Admin, Branch Type 1, Branch Type 2, and Kitchen branches while preserving their history. Resolve contract mismatches through explicit ADRs and versioned migrations; never silently change an API payload or inventory rule.

Required verification:

1. Build the NestJS workspace and all Avalonia solutions.
2. Start the server stack from a clean machine state using Docker Compose.
3. Apply PostgreSQL migrations through the explicit migration job.
4. Run unit, integration, contract, sync, authorization, and migration tests.
5. Run end-to-end request -> kitchen dispatch -> branch receipt.
6. Run mismatch -> Admin decision -> site application acknowledgement.
7. Replay every sync event and confirm no duplicated stock, revenue, payment, or recipe consumption.
8. Complete an offline branch sale and later sync it.
9. Generate and validate shift `.xlsx` reports; confirm no CSV path exists.
10. Build the three Windows installers in Windows CI and test upgrades without deleting SQLite data.
11. Validate release manifests, hashes, profile restrictions, backup/restore, and rollback.
12. Produce `docs/handoffs/INTEGRATION_REPORT.md` with commands, results, failures, and remaining production blockers.

Do not deploy production or merge to `main` until the owner reviews the integration report and explicitly approves it.

