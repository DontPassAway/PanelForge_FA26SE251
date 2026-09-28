---
name: panelforge-event-sourcing
description: Rules for PanelForge append-only versioning, rollback, audit trail and AI provenance. Use when implementing save/version/history/restore/rollback, audit logging, domain events, AI suggestion acceptance, or canvas/drag persistence in PanelForge, and when reviewing any change that could overwrite or delete history.
---

# PanelForge Versioning, History & Provenance

Related BRs: BR-01, BR-02, BR-08, BR-10, BR-11, BR-15, BR-18.

## Append-only model

```
V1 → V2 → V3            (saved versions)
Restore V1  ⇒  V4 = state of V1     (new head)
V1, V2, V3 stay intact.
```

- A save creates a **new revision row**. Never `UPDATE` or `DELETE` an existing revision.
- Rollback/restore is a **new command that appends** a revision copied from the target. It is never a delete of newer rows or a reset of a pointer that hides them.
- Reuse the existing pattern `Domain/Entities/Bible/BibleEntryRevision.cs`: `VersionNumber`, `SnapshotJson`, `ContentHash`, `AuthorId`, `EffectiveFromChapterNumber`, created only through a static `Create(...)` with guard clauses and private setters.
- Version numbers are monotonic per aggregate; compute `max + 1` inside the same transaction and protect it with a unique index `(ParentId, VersionNumber)`.
- Soft delete (`ISoftDelete`) is for live entities. It is not a substitute for history, and revision rows must not be soft-deleted.

## Audit trail (BR-18)

- `PanelForgeDbContext.SaveChangesAsync` already adds `AuditLog` rows for changed entities in the same save (see `AuditChangeSerializer`). Keep all state changes going through `SaveChangesAsync` so audit and change commit or fail together.
- Do not use `ExecuteUpdateAsync`/`ExecuteDeleteAsync`/raw SQL on audited entities unless you also write the audit record in the same transaction.
- If audit or provenance cannot be written, the operation must fail. Never swallow the exception.
- Never put secrets, tokens or passwords into audit payloads.

## Optimistic concurrency (BR-11)

- Editable aggregates carry a concurrency token (existing: `Chapter` via `IsConcurrencyToken()` in `ChapterConfiguration`).
- The client sends the version it edited; the handler compares it and EF raises `DbUpdateConcurrencyException` on conflict.
- Map conflicts to HTTP 409 with the current version so the UI can reload/merge. Never retry by blindly re-applying the client's stale state.

## AI provenance (BR-02, BR-10)

AI output enters as a **suggestion**. It becomes official working content only through an explicit human accept command. Before acceptance, persist:

- provider and model
- parameters (temperature, etc.) where applicable
- input context: prompt reference or hash (not raw secrets)
- the AISuggestion id
- accepting user, decision (accepted / rejected / modified) and timestamp
- degree of human modification where applicable

The accept handler fails if the provenance record is missing. `AiUsageRecord` tracks usage/cost only; it is not provenance.

## Canvas and drag interactions

- Persist the **committed final change** (drop, resize end, explicit save), not every pointer move.
- Frontend: debounce/commit on `pointerup`/`dragend`; do not send a request or domain event per `mousemove`.
- Backend: one command per committed change produces one audit entry / domain event (`ElementMovedEvent` etc.).

## Review red flags

- `Remove(...)`/`ExecuteDelete` on anything named `*Revision`, `*Version`, `*Log`, `*History`, `AuditLog`.
- Rollback implemented as "delete versions after N".
- Update handler that assigns fields without checking the expected version.
- AI result written straight into content tables.
