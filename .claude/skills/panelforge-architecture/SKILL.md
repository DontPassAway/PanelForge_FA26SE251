---
name: panelforge-architecture
description: Enforces PanelForge Clean Architecture layer boundaries (API → Application → Domain, Infrastructure → Application/Domain). Use when adding or moving any backend class, project reference, controller, handler, entity, EF configuration or external integration in PanelForge, and when reviewing a change for layering violations.
---

# PanelForge Architecture

## Dependency direction (verified in the .csproj files)

```
PanelForge.API ──────────► PanelForge.Application ──► PanelForge.Domain
      │                              ▲                        ▲
      └──► PanelForge.Infrastructure ┴────────────────────────┘
```

- `PanelForge.Domain` references nothing. Never add a reference from Domain to Application, Infrastructure, API, EF Core or ASP.NET.
- `PanelForge.Application` references only Domain. It must not reference Infrastructure or API.
- `PanelForge.Infrastructure` references Application + Domain and implements their ports.
- `PanelForge.API` is the composition root (`Program.cs`) and HTTP layer only.

Check before finishing: `grep -h "ProjectReference" src/*/*.csproj` must still show exactly this graph.

## What goes where

| Concern | Layer | Existing example |
|---|---|---|
| Entities, invariants, state transitions, domain events | Domain `Entities/<Area>/`, `Events/` | `Chapter.IncrementVersion()`, `BibleEntryRevision.Create(...)` |
| Use case (one Command/Query + Handler) | Application `Features/<Area>/Commands|Queries/<UseCase>/` | `Features/Chapters/Commands/UpdateChapter/UpdateChapterCommand.cs` |
| DTOs returned by use cases | Application `Features/<Area>/Models/` | `ChapterDtos.cs` |
| Ports for external/cross-cutting capabilities | Application `Common/Interfaces/{Persistence,Services,Authentication}` | `IPanelForgeDbContext`, `IEmailService`, `ICurrentUserService` |
| EF Core mapping, DbContext, audit interception | Infrastructure `Persistence/` | `PanelForgeDbContext.SaveChangesAsync`, `Configurations/Content/ChapterConfiguration.cs` |
| External providers (email, AI, storage, Firebase) | Infrastructure `ExternalServices/`, `Services/`, `Authentication/` | |
| HTTP routing, status codes, request binding, auth attributes | API `Controllers/`, `Common/` | `ChaptersController`, `[RequireWorkspaceRole]` |

## Controller rules

- Thin: bind request → build Command/Query → `await _mediator.Send(...)` → map `Result<T>` to an HTTP status.
- No business rules, no `DbContext`, no repository calls in controllers. None exists today (no documented exception), so do not introduce one.
- Current user id comes from claims (`ClaimTypes.NameIdentifier`), never from the request body.
- Workspace authorization uses `[RequireWorkspaceRole(...)]` plus handler-side checks (see `panelforge-security`).

## Rules for new code

1. Read the neighbouring feature folder first and copy its shape. Consistency beats a "better" pattern.
2. Do not add layers, folders or projects for symmetry (no empty `Repositories/` for every aggregate, no `Managers/`, no `Helpers/` dumping ground).
3. Handlers use `IPanelForgeDbContext` directly. Add a repository interface only when a query is genuinely reused by several handlers (the existing precedent is `IPageRepository`).
4. EF Core-specific code (`IEntityTypeConfiguration`, migrations, interceptors) lives in Infrastructure only.
5. Domain entities keep setters private and expose intent-revealing methods; handlers call those methods instead of assigning fields.
6. A new NuGet package needs a reason why the BCL, EF Core, MediatR or an existing package cannot do it.

## Review checklist

- [ ] Project reference graph unchanged, or the change is justified.
- [ ] No `using PanelForge.Infrastructure` in Application/Domain; no `using Microsoft.AspNetCore` in Domain/Application.
- [ ] Controller action ≤ binding + Send + result mapping.
- [ ] New interface justified per `panelforge-cqrs` ("when an interface is allowed").
- [ ] Business rules affected are listed (see `panelforge-business-rules`).
