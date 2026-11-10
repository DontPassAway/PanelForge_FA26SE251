using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Publisher.Models;

public sealed record PublisherChapterDto(
    Guid Id,
    Guid SeriesId,
    string SeriesTitle,
    Guid WorkspaceId,
    string WorkspaceName,
    decimal ChapterNumber,
    string? Title,
    DateOnly? TargetReleaseDate,
    long VersionVector,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? CreatedBy,
    int ScenesCount,
    int PagesCount,
    ChapterStatus Status,
    DateTime? PublishedAt = null,
    string? Outline = null,
    bool IsScriptFrozen = false,
    DateTime? ScriptFrozenAt = null,
    Guid? ScriptFrozenById = null,
    ScriptReopenStatus ScriptReopenStatus = ScriptReopenStatus.None,
    string? ScriptReopenReason = null,
    Guid? ScriptReopenRequestedById = null,
    DateTime? ScriptReopenRequestedAt = null,
    string? CoverUrl = null,
    string? ScriptContentHash = null
);

public sealed record PublisherSeriesDto(
    Guid Id,
    Guid WorkspaceId,
    string WorkspaceName,
    string Title,
    string? Synopsis,
    string Genre,
    string Format,
    ReadingDirection ReadingDirection,
    string? CoverUrl,
    int TotalChaptersCount,
    int ApprovedChaptersCount,
    int PublishedChaptersCount,
    DateTime? UpdatedAt
);

public sealed record PublisherWorkspaceDto(
    Guid Id,
    string Name,
    int SeriesCount,
    int ApprovedChaptersCount
);
