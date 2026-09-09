# Paste this into the first VPS chat

The Sugar ERP repository is located at `C:\sugar`. A prompt pack ZIP or extracted prompt pack is present in the workspace.

Prepare the prompt files yourself: extract the pack if required, place the prompt documents under `C:\sugar\docs\agent-prompts`, and place the supplied `AGENTS.md` at `C:\sugar\AGENTS.md`. Preserve existing files and inspect before overwriting. Do not ask me to create the layout manually.

Then read, in full:

1. Root `AGENTS.md`
2. `docs/agent-prompts/SHARED_PROJECT_CONTEXT.md`
3. `docs/agent-prompts/VPS_CHAT_PROMPT.md`
4. Any existing architecture/source documents referenced by those files

Follow `VPS_CHAT_PROMPT.md` as your assignment. You are the first of four implementation chats and own the shared Contract v1 checkpoint plus the VPS/Admin area.

The fixed stack is TypeScript/NestJS and PostgreSQL on the VPS, Docker Compose for local server development and VPS deployment, C#/Avalonia for native Windows desktop applications, SQLite/Entity Framework Core locally, `.xlsx` only, and GitHub Actions for CI/CD. Do not replace these technologies. The Admin frontend framework is pending my approval.

Develop locally first. Do not deploy or push until I approve the target. Begin by inspecting the workspace, organizing the supplied files, and asking me once for the final repository name, GitHub owner, and whether the remote repository already exists. Then proceed with the local Shared Contract v1 and VPS foundation work.

