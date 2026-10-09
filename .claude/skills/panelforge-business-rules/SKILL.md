---
name: panelforge-business-rules
description: Catalogue of PanelForge business rules BR-01..BR-24 plus the mandatory workflow for applying them. Use before implementing, changing, reviewing or simplifying any PanelForge feature, endpoint, entity, UI flow or test, so that affected BR IDs are identified, invariants are preserved and rules are tested.
---

# PanelForge Business Rules

## Mandatory workflow for every feature or fix

1. **List affected BR IDs** (from the table below) in your plan/response before editing code.
2. **Inspect the existing implementation**: entity methods, handler, controller, EF configuration, tests.
3. **Preserve invariants**: never weaken a rule to make a change smaller.
4. **Implement the smallest change** that satisfies the use case *and* the rules.
5. **Test the rules**: each touched BR gets at least one xUnit (backend) or Vitest (frontend) test for the happy path and the violation path.

If a rule seems to conflict with a request, stop and ask. Do not silently choose.

## Rules

| ID | Rule | Enforce by |
|---|---|---|
| BR-01 | Saved versions are append-only. Never silently overwrite history. | New revision row per save (pattern: `BibleEntryRevision`). No UPDATE/DELETE of revision rows. |
| BR-02 | AI output is a suggestion; a human must approve it. | AI result stored as suggestion; separate accept/reject command by a user. |
| BR-03 | Strict containment Series → Chapter → Scene → Page → Panel → Element. No orphans. | Required FKs; handler verifies parent exists **and** belongs to same Workspace. |
| BR-04 | Assets use SHA-256. Client hash is advisory; server recomputes and verifies. | Hash computed server-side from uploaded bytes; mismatch → reject. |
| BR-05 | One ScriptLine binds to at most one corresponding text Element. | Unique constraint + handler check. |
| BR-06 | Stage transitions pass required approval gates. | Transition command checks gate state before changing stage. |
| BR-07 | Two-level access: SystemRole = platform admin; WorkspaceRole from WorkspaceMember controls content. No WorkspaceMember = no workspace access. | `IWorkspaceAuthorizationService`; see `panelforge-security`. |
| BR-08 | Rollback creates a new head version; history is never deleted. | Restore Vn ⇒ append V(head+1) with Vn's state. |
| BR-09 | Expired preview links remain invalid. | Expiry checked on every access; never auto-extend. |
| BR-10 | AI provenance must exist before AI output becomes official working content. | Accept command fails if provenance record is missing. |
| BR-11 | Optimistic concurrency; never silently overwrite stale edits. | Concurrency token + expected version from client; `DbUpdateConcurrencyException` → 409. |
| BR-12 | Approved/Locked Chapters are read-only until explicitly reopened with a recorded reason. | Every content mutation checks chapter status; Reopen command requires reason. |
| BR-13 | Duplicate asset hashes reuse `Asset` through `AssetReference`. | Lookup by hash before storing bytes. |
| BR-14 | Assignments generate a notification before becoming active. | Notification created in same transaction before status = Active. |
| BR-15 | Series Bible entries are versioned by effective-from Chapter. | `BibleEntryRevision.EffectiveFromChapterNumber`. |
| BR-16 | Unreleased assets use short-lived revocable access with access logging. | Server-issued short-lived URL/token + access log row. |
| BR-17 | Preview links are recipient-bound, watermarked, revocable and logged. | `ExternalPreviewLink` checks + access log. |
| BR-18 | Every state-changing operation records actor, timestamp and change. If audit/provenance recording fails, reject the operation. | `PanelForgeDbContext.SaveChangesAsync` writes `AuditLog` in the same save; do not bypass it (no raw SQL / `ExecuteUpdate` on audited entities without explicit audit). |
| BR-19 | Stage dependencies apply per Page/Panel; independent items progress in parallel. | Gate evaluation scoped to the item, not the whole chapter. |
| BR-20 | Stages are left only after their gate passes. Acknowledged warnings require recorded reasons where applicable. | Transition command requires reason when warnings are acknowledged. |
| BR-21 | ConsistencyFinding triage: Confirmed, Rejected, Ignored, NeedsClarification. Ignored requires a reason. | Enum + reason validation. |
| BR-22 | A User may create a Workspace only when `CanCreateStudio` is granted. Creator automatically becomes Producer. | Create-workspace handler checks flag and adds creator `WorkspaceMember` (Producer) in the same save. |
| BR-23 | Users see only Workspaces where they have WorkspaceMember membership; non-members see only allowed public content. | All workspace queries filter by membership of current user. |
| BR-24 | Public Reader access is separate from internal Workspace access and from PreviewLink access. | Separate endpoints/policies; never reuse workspace auth for public reads or vice versa. |

## Known gaps to keep in mind (current code, do not "fix" outside a task)

- Only `Chapter` currently has a concurrency token (`ChapterConfiguration`). Other editable aggregates will need one when their edit flow is implemented (BR-11).
- There is no AI suggestion/provenance entity yet, only `AiUsageRecord`. Any AI-output feature must add provenance first (BR-02, BR-10).

When you touch one of these areas as part of a task, implement the rule; otherwise just mention the gap.

## Output format when planning

```
Affected BRs: BR-03 (containment), BR-11 (concurrency), BR-18 (audit)
Invariants kept: ...
Tests: ...
```
