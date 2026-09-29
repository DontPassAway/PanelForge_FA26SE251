using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Models;

public sealed record ChapterDto(
    Guid Id,
    Guid SeriesId,
    decimal ChapterNumber,
    string? Title,
    DateOnly? TargetReleaseDate,
    long VersionVector,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? CreatedBy,
    int ScenesCount,
    int PagesCount,
    ChapterStatus Status = ChapterStatus.InProduction,
    DateTime? PublishedAt = null
);

public sealed record ChapterSceneSummaryDto(
    Guid Id,
    int SceneNumber,
    string? Heading,
    string? Summary
);

public sealed record ChapterPageSummaryDto(
    Guid Id,
    int PageNumber,
    string LayoutFormat,
    int WidthPx,
    int HeightPx,
    int Dpi
);

public sealed record ChapterDetailDto(
    Guid Id,
    Guid SeriesId,
    decimal ChapterNumber,
    string? Title,
    DateOnly? TargetReleaseDate,
    long VersionVector,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? CreatedBy,
    IReadOnlyList<ChapterSceneSummaryDto> Scenes,
    IReadOnlyList<ChapterPageSummaryDto> Pages,
    ChapterStatus Status = ChapterStatus.InProduction,
    DateTime? PublishedAt = null
);

public sealed record CreateChapterRequest(
    decimal ChapterNumber,
    string? Title = null,
    DateOnly? TargetReleaseDate = null
);

public sealed record UpdateChapterRequest(
    string? Title,
    DateOnly? TargetReleaseDate,
    bool IncrementVersion = false
);
