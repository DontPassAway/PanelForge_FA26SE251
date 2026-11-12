using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Series.Models;

public sealed record SeriesDto(
    Guid Id,
    Guid WorkspaceId,
    string Title,
    string? Synopsis,
    ReadingDirection ReadingDirection,
    Guid? PipelineDefinitionId,
    string Genre,
    string Format,
    string? ReleaseScheduleJson,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? CreatedBy,
    int ChaptersCount,
    string? StoryArc = null,
    string? CoverUrl = null,
    string? AvatarUrl = null,
    string? BannerUrl = null,
    bool IsCompleted = false
);

public sealed record SeriesChapterSummaryDto(
    Guid Id,
    decimal ChapterNumber,
    string? Title,
    DateOnly? TargetReleaseDate,
    long VersionVector,
    DateTime CreatedAt
);

public sealed record SeriesDetailDto(
    Guid Id,
    Guid WorkspaceId,
    string Title,
    string? Synopsis,
    ReadingDirection ReadingDirection,
    Guid? PipelineDefinitionId,
    Guid? BibleId,
    string Genre,
    string Format,
    string? ReleaseScheduleJson,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? CreatedBy,
    IReadOnlyList<SeriesChapterSummaryDto> Chapters,
    string? StoryArc = null,
    string? CoverUrl = null,
    string? AvatarUrl = null,
    string? BannerUrl = null
);

public sealed record CreateSeriesRequest(
    string Title,
    string? Format = "MANGA",
    ReadingDirection? ReadingDirection = null,
    string? Synopsis = null,
    string? Genre = null,
    string? StoryArc = null,
    Guid? PipelineTemplateId = null
);

public sealed record UpdateSeriesRequest(
    string Title,
    string? Synopsis,
    ReadingDirection ReadingDirection,
    Guid? PipelineDefinitionId,
    string? Genre = null,
    string? Format = null,
    string? ReleaseScheduleJson = null,
    string? StoryArc = null,
    bool? IsCompleted = null
);
