---
name: panelforge-security
description: PanelForge security rules - Workspace as tenant boundary, server-side WorkspaceRole authorization, preview link and asset access, secret handling and logging hygiene. Use when adding or changing any endpoint, handler, query, auth logic, preview link, asset/export access, AI provider credential flow, logging, or frontend permission-dependent UI in PanelForge.
---

# PanelForge Security

Related BRs: BR-07, BR-09, BR-16, BR-17, BR-22, BR-23, BR-24.

## Workspace is the tenant boundary

Every workspace-scoped operation must verify, **server-side**, in this order:

```
Authenticated user (claims)
→ WorkspaceMember exists for (user, workspace)
→ WorkspaceRole permits the action
→ the requested entity belongs to that same workspace (walk the containment chain)
→ entity/state constraints pass (e.g. chapter not Locked, BR-12)
```

Never trust: hidden UI controls, route visibility, IDs supplied by the client, or a WorkspaceRole sent by the frontend.

`SystemRole` (Administrator) handles platform administration. It does **not** grant access to creative content in a workspace unless an explicit, documented use case says so.

## How the current code does it, and its limits

- API: `[RequireWorkspaceRole(WorkspaceRole.Producer, ...)]` (`API/Common/Attributes/RequireWorkspaceRoleAttribute.cs`) resolves the workspace from route values `workspaceId`, `id`, `seriesId` or `chapterId` and calls `IWorkspaceAuthorizationService`.
- Limits you must handle yourself:
  - Routes keyed by `sceneId`, `pageId`, `panelId`, `elementId`, etc. are **not** resolved by the filter. The handler must load the entity, walk up to its workspace, and check membership/role via `IWorkspaceAuthorizationService`.
  - A route parameter named `{id}` is interpreted as a **workspace id** by the filter. Do not use `[RequireWorkspaceRole]` on routes where `{id}` is some other entity; name the parameter explicitly (`{pageId}`) and authorize in the handler.
  - When a body contains a second id (e.g. target parent for a move), verify that it belongs to the same workspace too.
- Queries listing workspaces or content must filter by the current user's membership (BR-23), not return everything and filter in the UI.

## Preview links & public access

- Validate on every request: target Chapter/version, expiry (BR-09), revoked state, and recipient binding where required (BR-17). Log each access.
- Public Reader access, internal Workspace access and PreviewLink access are three separate paths (BR-24). Do not reuse one's authorization for another.

## Assets & exports

- Serve unreleased artwork and export files through short-lived, server-issued, revocable access (BR-16). Never return private object-store paths or long-lived public URLs.
- Recompute SHA-256 server-side (BR-04).

## Secrets & logging

- Never log passwords, access/refresh tokens, private API keys, provider credentials, OTP codes or full signed URLs.
- AI provider keys are stored encrypted via `IAiKeyProtector`; never return them to the client after creation. Return a masked form at most.
- Do not read or print `.env`, `firebase-key.json`, user-secrets, `*.pfx/*.pem/*.key`. Configuration values come from configuration providers, not hard-coded strings.

## Frontend

- UI may hide actions the role cannot perform, but that is UX only. The backend decision is authoritative; handle 401/403/409 gracefully.
- Do not store tokens in places readable by third-party scripts beyond what the current auth flow already does; do not put tokens in URLs.
- Only `VITE_*` values that are safe to be public may be read in the browser (e.g. Firebase web config). Server secrets never go into Vite env.

## Checklist for every endpoint

- [ ] `[Authorize]` present (or an explicit documented anonymous endpoint).
- [ ] Workspace resolved and membership + role checked server-side.
- [ ] Every id in route/body verified to belong to that workspace.
- [ ] State constraints (locked/approved chapter, gates) checked.
- [ ] Failure responses don't leak existence of other workspaces' data beyond the existing 404/403 convention.
- [ ] Tests: non-member → 403/404, wrong role → 403, cross-workspace id → rejected.
