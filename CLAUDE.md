# CLAUDE.md – PanelForge Backend

ASP.NET Core Web API (.NET 9) · Clean Architecture · CQRS with MediatR · EF Core + PostgreSQL · xUnit.

Solution: `PanelForge.slnx` · Projects: `src/PanelForge.{API,Application,Domain,Infrastructure}`, plus the AI projects `src/PanelForge.AI` and `src/PanelForge.AI.Contracts`; tests in `test/PanelForge.UnitTests`.

```bash
dotnet build PanelForge.slnx
dotnet test PanelForge.slnx
```

Frontend ↔ Backend coordination follows `.agents/CODEBASE_MAP.md` and feature DTOs in `PanelForge.Application/Features/<Domain>/`. Expose clean Swagger schemas and maintain DTO parity with Frontend and Mobile.

## Central Knowledge Base & Spec (Tất cả tài liệu đọc tại Hub)

> 🚨 **QUY TẮC CỐT LÕI:** Tất cả tài liệu đặc tả, nghiệp vụ, hiểu biết hệ thống, kiến trúc và kế hoạch triển khai **ĐƯỢC ĐỌC TẬP TRUNG TẠI `../panelforge-hub/docs/`**, KHÔNG đọc tại các repo riêng lẻ.

- **Master Spec V2.6:** `../panelforge-hub/docs/V2.6/PANELFORGE_MASTER_SPECIFICATION_V2_6.md` (quyết định Q1–Q14, BR-xx, 5 vai trò, 5 luồng CF1–CF5). Trích dẫn mã BR-xx / Module trong commit.
- **Kế hoạch triển khai (Plans):** `../panelforge-hub/docs/note/PANELFORGE_V2_6_IMPLEMENTATION_PLAN.md` và `../panelforge-hub/docs/note/plans/`. Không tạo hay lưu file plan trong repo này.
- **Kiến trúc & Sơ đồ:** `../panelforge-hub/docs/architecture/` (Clean Architecture, storage, diagrams, OpenApi).
- **Thuật ngữ & State Machines:** `../panelforge-hub/docs/standards/` (Ubiquitous Language, State Machine standards).
- **Team Workboard:** `../panelforge-hub/docs/tracking/TEAM_WORKBOARD.md`. Khi bắt đầu task và khi merge, cập nhật trạng thái kèm thay đổi API/DB/enum cho cả nhóm.
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

1. PanelForge Business Rules / requirements (`panelforge-business-rules`, `panelforge-hub/docs/V2.6/PANELFORGE_MASTER_SPECIFICATION_V2_6.md`)
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

## Git workflow (bắt buộc cho AI Agents)

- **Nhánh làm việc: `{work-branch}`** — nhánh do người code trên máy này chọn (mỗi người push nhánh riêng), lưu ở `git config --local panelforge.workBranch` (chỉ trên máy này, không commit). Đầu phiên chạy `git config --local --get panelforge.workBranch`: có giá trị → dùng, không hỏi lại; trống → hỏi người dùng tên nhánh rồi lưu bằng `powershell -File ../panelforge-hub/.agents/scripts/work_branch.ps1 -Action set -Branch <tên>`; repo đang ở nhánh khác → hỏi trước khi checkout. Không commit/push lên `main` hay nhánh của người khác; không tạo nhánh `feature/*` để làm việc thường ngày.
- **Một agent:** làm trực tiếp trên `{work-branch}` trong repo chính (`git checkout {work-branch}`), không tạo worktree.
- **Nhiều agent song song (multi-agent / subagent / session song song):** mỗi agent làm trong **git worktree riêng**, tách nhánh con từ `{work-branch}`:
  - `git worktree add ../.worktrees/<repo>-<task> -b {work-branch}-<task> {work-branch}`
  - Mỗi agent chỉ sửa file thuộc task của mình; hai agent không được sửa cùng file.
  - Không agent nào sửa trực tiếp working tree chính khi agent khác đang chạy.
  - Khi xong: commit trong worktree → `git checkout {work-branch} && git merge --no-ff {work-branch}-<task>` → giải quyết conflict, build → `git worktree remove <path>` và `git branch -d {work-branch}-<task>`. Không push nhánh `{work-branch}-<task>` lên remote.
- **Lấy code mới:** khi working tree sạch, `git pull origin {work-branch}` (còn thay đổi thì commit hoặc `git stash` rồi `stash pop` sau khi pull).
- **Push:** chỉ `git push origin {work-branch}` khi hoàn thành 1 module lớn hoặc khi user yêu cầu trực tiếp. Không push sau mỗi prompt/commit nhỏ.
- Không force-push, không rebase lại lịch sử đã push trên `{work-branch}`.
- **Conflict:** giữ thay đổi của cả hai bên khi không loại trừ nhau; với `*ModelSnapshot.cs` của EF, sau khi gộp phải kiểm tra bằng `dotnet ef migrations add` thử (phải ra migration rỗng). Build/test trước khi commit merge.
- **Commit message:** theo Conventional Commits (`panelforge-hubs/web/public/docs/note/GIT_COMMIT_CONVENTION.md`): `<type>(<scope>): <mô tả>` — type ∈ feat|fix|docs|style|refactor|perf|test|chore; header ≤ 72 ký tự, động từ mệnh lệnh, viết thường, không dấu chấm cuối; body giải thích *why/how*, trích mã BR-xx nếu có. Merge commit: `chore(merge): sync origin/{work-branch} into {work-branch}`.
- Không dùng `--no-verify`, không `push --force` lên nhánh dùng chung.
- **Sau khi sửa code, cập nhật `panelforge-hubs` (nhánh `main`) và push:**
  1. `git -C ../panelforge-hubs pull --ff-only origin main`.
  2. Thêm một dòng lên đầu §3 "Đã xong" của `docs/tracking/TEAM_WORKBOARD.md` (ngày, người, repo BE/FE, việc, commit hash trên `{work-branch}`, ảnh hưởng API/DB/enum); xoá dòng của mình ở §2 nếu có.
  3. Chạy `node scripts/sync-hub.mjs` để đồng bộ `hub-state.json`, `AUDIT_LOGS.md` và `web/public/docs`.
## Git Branch & Multi-Agent Workflow (THBMinh)

### Luồng nhánh (promotion flow)

```
THBMinh  ──merge──▶  dev  ──merge──▶  stg  ──(stg chạy OK)──merge──▶  main
(code)              (tích hợp)        (môi trường giống production)     (production)
```

- **`THBMinh`**: nhánh code. Mọi thay đổi đều được commit ở đây (có ở cả BE `PanelForge_FA26SE251` và FE `panelforge-fe`).
- **`dev`**: nhánh tích hợp. Khi xong việc trên `THBMinh` → merge `THBMinh` vào `dev`.
- **`stg`**: staging, environment giống production. Chỉ nhận merge từ `dev`.
- **`main`**: production. Chỉ nhận merge từ `stg`, và **chỉ sau khi `stg` đã được xác nhận chạy OK**.
- Không bao giờ commit trực tiếp lên `dev`, `stg`, `main`; không merge nhảy cóc (ví dụ `THBMinh` → `main`, `dev` → `main`). Không force-push, không rebase lịch sử đã push.
- Agent chỉ tự merge/push tới `dev`. Đẩy `dev` → `stg` và `stg` → `main` cần người dùng xác nhận rõ ràng.

### Làm việc với agent

- **Một agent**: làm trực tiếp trên `THBMinh` (`git checkout THBMinh`).
- **Nhiều agent (multi-agent / subagent / session song song)**: mỗi agent làm trong **git worktree riêng**, trên nhánh con tách từ `THBMinh`:
  - `git worktree add ../.worktrees/<repo>-<task> -b THBMinh-<task> THBMinh`
  - Mỗi agent chỉ sửa file thuộc task của mình; hai agent không sửa cùng file; không sửa working tree chính khi agent khác đang chạy.
- **Khi hoàn thành task**:
  1. Commit trong worktree → `git checkout THBMinh && git merge --no-ff THBMinh-<task>`, giải quyết conflict, chạy build/test.
  2. `git push origin THBMinh`.
  3. Merge vào dev: `git checkout dev && git pull && git merge --no-ff THBMinh && git push origin dev`, rồi quay lại `THBMinh`.
  4. Dọn dẹp: `git worktree remove <path>` và `git branch -d THBMinh-<task>`.
