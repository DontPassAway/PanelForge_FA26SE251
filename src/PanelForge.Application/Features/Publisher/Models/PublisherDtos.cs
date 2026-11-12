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
    string? ScriptContentHash = null,
    bool IsLocked = false,
    decimal PriceVnd = 0m
);

public sealed record PublisherLatestChapterDto(
    Guid ChapterId,
    decimal ChapterNumber,
    string? Title,
    ChapterStatus Status,
    DateTime? UpdatedAt
);

public sealed record PublisherSeriesListItemDto(
    Guid SeriesId,
    Guid Id,
    string Title,
    string? CoverImageUrl,
    string? CoverUrl,
    string Status,
    IReadOnlyList<string> Genres,
    string Genre,
    string AgeRating,
    string? Synopsis,
    Guid WorkspaceId,
    string WorkspaceName,
    int TotalChaptersCount,
    int ApprovedChaptersCount,
    int PublishedChaptersCount,
    PublisherLatestChapterDto? LatestChapter,
    DateTime? UpdatedAt,
    string Format,
    ReadingDirection ReadingDirection
);

public sealed record PublisherManagedChapterDto(
    Guid ChapterId,
    Guid Id,
    decimal ChapterNumber,
    string? Title,
    ChapterStatus Status,
    DateTime? PublishAt,
    DateTime? PublishedAt,
    int PageCount,
    int PagesCount,
    DateTime? ApprovedAt,
    string? ApprovedBy,
    bool IsLocked = false,
    decimal PriceVnd = 0m
);

public sealed record PublisherSeriesDetailDto(
    Guid SeriesId,
    Guid Id,
    string Title,
    string? CoverImageUrl,
    string? CoverUrl,
    Guid WorkspaceId,
    string WorkspaceName,
    string? ProducerName,
    IReadOnlyList<string> Genres,
    string Genre,
    string AgeRating,
    string Status,
    string? Logline,
    string? Synopsis,
    IReadOnlyList<PublisherManagedChapterDto> Chapters
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
