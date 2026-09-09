# Sugar ERP — Four-Chat Build Order

This pack replaces the slow one-chat/subagent workflow.

## Put these files in the repository

Copy this entire folder into `C:\sugar\docs\agent-prompts\`. Copy `AGENTS.md` to `C:\sugar\AGENTS.md` so every chat automatically receives the repository-wide rules.

## Use Codex worktrees

Do not run four chats against one writable working directory. Use one Codex worktree per chat so Git isolates their edits.

## Start order

1. Paste `START_FIRST_VPS_CHAT.md` into the first chat. It prepares the files and then follows `VPS_CHAT_PROMPT.md`.
2. The VPS chat creates the repository skeleton and freezes `docs/contracts/CONTRACT_V1.md`.
3. Review and merge/commit Contract v1 to the base branch.
4. Start the Branch Type 1, Branch Type 2, and Kitchen chats from that exact Contract v1 commit, each in its own worktree.
5. Continue the VPS/Admin work in parallel.
6. After the four module branches pass their own tests, use `FINAL_INTEGRATION_PROMPT.md` in a clean integration worktree.

## Files each chat receives

Every chat must read:

- Root `AGENTS.md`
- `SHARED_PROJECT_CONTEXT.md`
- `docs/contracts/CONTRACT_V1.md` once it exists
- Its own module prompt

The desktop chats must stop if Contract v1 is missing. They must not invent private API contracts.

## Docker boundary

The VPS runtime is Dockerized: NestJS API/worker, PostgreSQL, and later the approved Admin frontend/reverse proxy. Local development must start with Docker Compose. Production deployment is GitHub-driven and must be runnable on the VPS with documented scripts.

Avalonia is a native Windows desktop GUI. Branch and Kitchen applications are packaged as Windows installers rather than run inside Linux containers. Their local database is SQLite and requires no separately installed database server.
