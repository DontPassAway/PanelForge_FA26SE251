---
name: panelforge-cqrs
description: Applies PanelForge's MediatR CQRS conventions - one use case, one Command or Query, one Handler, returning Result<T> - and blocks per-feature Service/IService wrappers. Use when creating or changing a backend use case, endpoint, command, query or handler, or when tempted to add an XxxService / IXxxService.
---

# PanelForge CQRS (MediatR)

## Default shape

One use case → one `Command` or `Query` → one `Handler`, in the same file, in its own folder:

```
Features/<Area>/Commands/<Verb><Noun>/<Verb><Noun>Command.cs   // record + handler
Features/<Area>/Queries/Get<Noun>By<X>/Get<Noun>By<X>Query.cs  // record + handler
Features/<Area>/Models/<Area>Dtos.cs
```

Follow the existing file `Features/Chapters/Commands/UpdateChapter/UpdateChapterCommand.cs`:

```csharp
public sealed record UpdateChapterCommand(Guid ChapterId, Guid UserId, ...) : IRequest<Result<ChapterDto>>;

public sealed class UpdateChapterCommandHandler : IRequestHandler<UpdateChapterCommand, Result<ChapterDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    // load → call domain methods → SaveChangesAsync → map DTO → Result.Success
}
```

- Commands change state; Queries never change state. Use `AsNoTracking()` in queries.
- The acting user id is a Command field that the controller fills from claims (`UserId`).
- Return `Result<T>.Failure(message)` for expected business failures. Throw only for programmer errors or truly exceptional conditions.
- The Handler is the application use-case boundary: it loads aggregates, checks authorization/containment, calls domain methods, saves once.
- Put invariants inside the entity method (e.g. `chapter.UpdateDetails(...)`). The handler orchestrates; it does not reimplement rules.

## Forbidden by default

Do **not** create a service merely to wrap a handler:

`ISeriesService/SeriesService`, `IChapterService/ChapterService`, `IBibleService/BibleService`, `IPanelService`, ...

Do not call one handler from another through `IMediator`. Extract a shared domain method or a private static helper instead.

## When an interface IS allowed

Only for a genuinely reusable or cross-cutting capability, typically an external boundary:

storage · email · AI provider · clock/time · current-user context · hashing · signed URL generation · external integrations · background jobs · workspace authorization lookups.

Existing ones (reuse them first): `IPanelForgeDbContext`, `IUnitOfWork`, `IPageRepository`, `ICurrentUserService`, `IEmailService`, `IAiKeyProtector`, `IAiProviderValidator`, `IAiUsageService`, `IWorkspaceAuthorizationService`, `IPasswordHasher`, `IJwtTokenGenerator`, `ITotpService`, `IExternalIdentityVerifier`.

Before adding a new interface, write one sentence in the PR/response answering: **"Why can't the Handler own this orchestration itself?"** Acceptable answers: it crosses a process/network boundary, it must be swapped in tests, or ≥2 handlers need the identical logic. "Cleaner" or "for symmetry" is not acceptable.

Note: a port with a single Infrastructure implementation (e.g. `IEmailService`) is required by Clean Architecture's dependency inversion. It is not an "unrequested abstraction" and Ponytail must not remove it.

## Validation

There is no MediatR validation pipeline behavior in the project today. Validate inputs in the handler or in the domain factory/method (`ArgumentException.ThrowIfNullOrWhiteSpace`, guard clauses). Do not add FluentValidation or a pipeline behavior unless the team decides to adopt it project-wide.

## Checklist

- [ ] One Command/Query + one Handler per use case, in the feature folder.
- [ ] Controller only sends it through `IMediator`.
- [ ] No new `XxxService` wrapping handler logic; any new interface has its justification sentence.
- [ ] Query does not mutate; command saves once.
- [ ] Affected BR IDs listed and tested (`panelforge-business-rules`).
