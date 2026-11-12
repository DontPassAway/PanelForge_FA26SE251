---
name: panelforge-business-rules
description: Catalogue of PanelForge business rules BR-01..BR-34 (aligned with Master Spec V2.6) plus the mandatory workflow for applying them. Use before implementing, changing, reviewing or simplifying any PanelForge feature, endpoint, entity, UI flow or test, so that affected BR IDs are identified, invariants are preserved and rules are tested.
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
| BR-06 | Stage transitions pass required approval gates (Four-eyes rule). | Transition command checks gate state; `ReviewerId != AssigneeId` and `ReviewerId != SubmitterId` (403). |
| BR-07 | Two-level access: SystemRole = platform admin; WorkspaceRole from WorkspaceMember controls content. No WorkspaceMember = no workspace access. | `IWorkspaceAuthorizationService`; see `panelforge-security`. |
| BR-08 | Rollback creates a new head version; history is never deleted. | Restore Vn ⇒ append V(head+1) with Vn's state. |
| BR-09 | Expired preview links remain invalid. | Expiry checked on every access; never auto-extend. |
| BR-10 | AI provenance must exist before AI output becomes official working content. | Accept command fails if provenance record is missing. |
| BR-11 | Optimistic concurrency; never silently overwrite stale edits. | Concurrency token + expected version from client; `DbUpdateConcurrencyException` → 409. |
| BR-12 | Approved/Locked Chapters are read-only until explicitly reopened with a recorded reason. | Every content mutation checks chapter status; Reopen command requires reason. |
| BR-13 | Duplicate asset hashes reuse `Asset` through `AssetReference`. | Lookup by hash before storing bytes. |
| BR-14 | Assignments generate a notification before becoming active. | Notification created in same transaction before status = Active. |
| BR-15 | Series Bible entries are versioned by effective-from Chapter. | `BibleEntryRevision.EffectiveFromChapterNumber`; `EffectiveRevisionAt(N)`. |
| BR-16 | Unreleased assets use short-lived revocable access with access logging. | Server-issued short-lived URL/token + access log row. |
| BR-17 | Preview links are recipient-bound, watermarked, revocable and logged. | `ExternalPreviewLink` checks + watermark + access log. |
| BR-18 | Every state-changing operation records actor, timestamp and change. If audit/provenance recording fails, reject the operation. | `PanelForgeDbContext.SaveChangesAsync` writes `AuditLog` in the same save; do not bypass it. |
| BR-19 | Stage dependencies apply per Page/Panel; Pencil completes before Ink and Lettering open in parallel. | Gate evaluation scoped to item; Pencil -> (Ink ∥ Lettering) -> Gate 2. |
| BR-20 | Stages are left only after their gate passes. Technical Lettering checks (Overflow, Reading order, Key artwork overlap). | Geometry guard calculations; acknowledged warnings require recorded reasons. |
| BR-21 | ConsistencyFinding triage & tagging. `@Tag` in script requires CanonLocked in Bible. | Enum + reason validation; Gate 1 checks canon status of tagged entities. |
| BR-22 | A User may create a Workspace only when `CanCreateStudio` is granted. Creator automatically becomes Producer. | Create-workspace handler checks flag and adds creator `WorkspaceMember` (Producer). |
| BR-23 | Users see only Workspaces where they have WorkspaceMember membership; non-members see only allowed public content. | All workspace queries filter by membership of current user. |
| BR-24 | Public Reader access is separate from internal Workspace access and from PreviewLink access. | Separate endpoints/policies; never reuse workspace auth for public reads. |
| BR-25 | Series Bible immutability (`CanonLocked`). No `/unlock`; changes require `POST /api/bible/{id}/supersede`. | Unlock endpoint removed; supersede creates new revision row. |
| BR-26 | Gate 2 Quality Review four-eyes validation & change requests. | `ReviewerId != AssigneeId` & `SubmitterId`; rework requires bounding box + comment. |
| BR-27 | Artist Self-Rollback & Panel-level error isolation. | Artist can rollback Ink -> Pencil before review; rework sets `CheckBalloonPlacement` flag. |
| BR-28 | Gate 3 ChapterLockGate & Edition release gate. | Producer locks chapter only when 100% pages Approved, 0 CRs, 0 blocking findings, 0 flags. Edition release requires ChapterApproved + EditorApproved. |
| BR-29 | Gate 1 single-action "Approve Script & Start Production". | Script frozen (`is_script_frozen = true`); chapter transitions PreProduction -> InProduction; assignment plan confirmed. |
| BR-30 | Mid-flight scope transfer (Carve-out). | Producer reassigns pages/panels from Artist A -> B; revokes A, grants B, moves open CRs to B. |
| BR-31 | Artist default lettering & Letterer tool isolation. | Artist has `graphic:write` and `letter:write` by default; Letterer only has `letter:write` (layout/graphic locked -> 403). |
| BR-32 | Publisher boundary & Producer 360° Dashboard. | Publisher cannot mutate production content (403); Producer views readership metrics and VNĐ revenue; royalty split out-of-scope. |
| BR-33 | Real-money VNĐ chapter edition monetization & preview pages. | Prices, wallet, transactions in VNĐ; Publisher sets `is_paid`, `price_vnd`, `free_preview_pages` per `ChapterEdition(chapter, lang)`; no tokens/VIP. |
| BR-34 | Balloon localization & Language Editions. | Shared balloon geometry `{x,y,w,h}` with per-language text; Gate 2 checks original language; editions reviewed by Editor & approved by Producer; reader unlocks per edition. |

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
