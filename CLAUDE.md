# CLAUDE.md – PanelForge Backend

ASP.NET Core Web API (.NET 9) · Clean Architecture · CQRS with MediatR · EF Core + PostgreSQL · xUnit.

Solution: `PanelForge.slnx` · Projects: `src/PanelForge.{API,Application,Domain,Infrastructure}`, plus the AI projects `src/PanelForge.AI` and `src/PanelForge.AI.Contracts`; tests in `test/PanelForge.UnitTests`.

```bash
dotnet build PanelForge.slnx
dotnet test PanelForge.slnx
```

Frontend ↔ Backend coordination happens through `../../HANDOFF.md` (see the workspace-level `CLAUDE.md`).

## PanelForge Agent Rules

1. Read existing implementation before writing code.
2. Identify the affected Use Case, Business Rules (BR-xx) and Requirements.
3. Follow Clean Architecture and MediatR CQRS.
4. The Handler is the application use-case boundary by default.
5. Do not create a service/interface per feature.
6. Prefer reuse over new abstractions.
7. Do not modify unrelated code.
8. Preserve append-only provenance.
9. Preserve optimistic concurrency.
10. Enforce Workspace authorization server-side.
11. AI output requires a human decision.
12. Write/update automated tests for business-rule changes.
13. Do not introduce dependencies unless existing platform/project dependencies cannot solve the problem.
14. Do not read or expose secrets unnecessarily (`.env`, `firebase-key.json`, user-secrets, keys, tokens, DB passwords).
15. Do not send source code or local files to unknown external services.
16. Ask before enabling a new network-connected MCP server.

## Precedence when instructions conflict

1. PanelForge Business Rules / requirements (`panelforge-business-rules`, `docs/requirements/requirements.md`)
2. Security and data integrity
3. Existing project architecture (what the code already does)
4. PanelForge custom skills (`.claude/skills/panelforge-*`)
5. Upstream engineering skills (`.claude/skills/*` from addyosmani/agent-skills)
6. Ponytail simplification

A lower level never overrides a higher one. Ponytail must never override a mandatory requirement.

## Intent → skill routing

| Intent | Skills |
|---|---|
| Requirement clarification | `interview-me`, `idea-refine`, `spec-driven-development` |
| Planning | `planning-and-task-breakdown` |
| Backend implementation | `incremental-implementation`, `api-and-interface-design`, `panelforge-architecture`, `panelforge-cqrs`, `panelforge-business-rules` |
| Versioning / provenance | `panelforge-event-sourcing`, `panelforge-business-rules` |
| Security | `security-and-hardening`, `panelforge-security` |
| Frontend | `frontend-ui-engineering` |
| Testing | `test-driven-development`, `browser-testing-with-devtools`, `panelforge-tooling` |
| Debugging | `debugging-and-error-recovery` |
| Performance | `performance-optimization` |
| Review | `code-review-and-quality`, `code-simplification`, Ponytail review (`/ponytail-review`) |
| CI/CD | `git-workflow-and-versioning`, `ci-cd-and-automation` |

## Skills ↔ MCP servers

MCP servers configured for this machine (Claude Code user scope): `playwright` (isolated, headless), `postgres` (read-only, `panelforge_dev`), `github` (read-only + lockdown), `context7` (library docs), `sequential-thinking`, `filesystem` (only `AI_Workspace`). Load `panelforge-tooling` whenever a skill step needs one of them.

- **Browser work defaults to Playwright MCP**, including where `browser-testing-with-devtools` / `test-driven-development` mention Chrome DevTools (tool mapping in `panelforge-tooling`).
- **Performance work uses Chrome DevTools MCP** (from the ECC plugin, `mcp__plugin_ecc_chrome-devtools__*`): performance trace, `lighthouse_audit`, CPU/network throttling, heap snapshots. Only for `performance-optimization` and the performance section of `browser-testing-with-devtools`. Never with `--autoConnect` / `--browser-url`.
- Library/framework API questions (MediatR, EF Core, ASP.NET Core, React, Tailwind, TanStack, dnd-kit) → `context7` before relying on memory.
- Business-rule / schema checks (BR-01, 03, 05, 08, 11, 13, 18) → `postgres` SELECT/EXPLAIN only; DB names are snake_case.
- PR/issue/branch context for review and planning → `github` (read-only; writes are done manually by the user).
- Multi-step planning/debugging → `sequentialthinking`.

## Resolved conflicts between skills

- **Ponytail "no interface with one implementation"**: does not apply to Clean Architecture ports in `Application/Common/Interfaces` (e.g. `IEmailService`, `IPanelForgeDbContext`). Dependency inversion is a project requirement.
- **Ponytail "fewest files"**: one Command/Query + Handler per use case in its own feature folder is the project convention; keep it.
- **Ponytail "one assert-based self-check, no frameworks"**: tests use the existing xUnit project (`test/PanelForge.UnitTests`), not ad-hoc `demo()` checks.
- **Ponytail "minimal output"**: still list affected BR IDs and tests in the plan/response.
- **Ponytail never removes**: validation, authorization, audit logging, append-only history, concurrency protection, provenance, security checks, human approval of AI output.
- **`browser-testing-with-devtools` assumes Chrome DevTools MCP for everything**: here Chrome DevTools MCP (provided by the ECC plugin, dedicated profile) is used only for performance measurement; Playwright MCP (`--isolated --headless`) stays the default. Do not install any additional browser MCP without asking (rule 16).
- **`ci-cd-and-automation` / `performance-optimization` examples use Node tooling (`npx tsc`, Prisma, Vercel)**: translate to this stack (`dotnet build`, `dotnet test`, EF Core migrations, existing `.github/workflows`).
- **`git-workflow-and-versioning`**: follow the team's existing branch names; never force-push shared branches or skip hooks.

## Ponytail

Installed as a Claude Code plugin at project scope (`.claude/settings.json`, marketplace pinned to `v4.10.0`). Default mode is **lite** (`PONYTAIL_DEFAULT_MODE=lite`): build what is asked and name the lazier alternative in one line. Do not switch the project default to `full` or `ultra`. `/ponytail-review` may be used during review, subject to the precedence above.

## Agent configuration files

- `.claude/skills/` – 16 upstream skills (addyosmani/agent-skills, MIT) + 6 `panelforge-*` skills. Upstream copies are unmodified; to update, re-copy from `agent-tools/agent-skills/skills/<name>` (local clone of https://github.com/addyosmani/agent-skills, git-ignored).
- `.claude/references/` – checklists referenced by upstream skills as `../../references/*.md`.
- `.claude/settings.json` – shared: Ponytail plugin, `PONYTAIL_DEFAULT_MODE`, deny rules for reading secret files.
- `.claude/settings.local.json` – personal, not committed.
