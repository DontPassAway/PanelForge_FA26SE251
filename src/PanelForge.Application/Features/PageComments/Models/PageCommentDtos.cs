using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.PageComments.Models;

public sealed record PageCommentDto(
    Guid Id,
    Guid SeriesId,
    Guid ChapterId,
    decimal ChapterNumber,
    string? ChapterTitle,
    Guid? PageId,
    int? PageNumber,
    Guid? PanelId,
    int? PanelNumber,
    Guid? AssetReferenceId,
    double? PinX,
    double? PinY,
    string Message,
    Guid AuthorUserId,
    string AuthorName,
    PageCommentStatus Status,
    string? ResolutionNote,
    Guid? ResolvedByUserId,
    string? ResolvedByName,
    DateTime? ResolvedAt,
    Guid? StageTaskId,
    string? StageName,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record CreatePageCommentRequest(
    Guid? PageId = null,
    Guid? PanelId = null,
    Guid? AssetReferenceId = null,
    double? PinX = null,
    double? PinY = null,
    string Message = "");

public sealed record ResolvePageCommentRequest(string? Note = null);

public sealed record ConvertPageCommentToReworkRequest(Guid StageTaskId, string Reason);
