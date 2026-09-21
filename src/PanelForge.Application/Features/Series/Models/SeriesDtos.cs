using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Series.Models;

public sealed record SeriesDto(
    Guid Id,
    Guid WorkspaceId,
    string Title,
    string? Synopsis,
    ReadingDirection ReadingDirection,
    Guid? PipelineDefinitionId,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? CreatedBy,
    int ChaptersCount
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
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? CreatedBy,
    IReadOnlyList<SeriesChapterSummaryDto> Chapters
);

public sealed record CreateSeriesRequest(
    string Title,
    string? Synopsis,
    ReadingDirection ReadingDirection = ReadingDirection.RightToLeft,
    Guid? PipelineDefinitionId = null
);

public sealed record UpdateSeriesRequest(
    string Title,
    string? Synopsis,
    ReadingDirection ReadingDirection,
    Guid? PipelineDefinitionId
);
