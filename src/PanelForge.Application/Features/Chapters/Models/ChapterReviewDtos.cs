using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Models;

public sealed record ChapterReviewDto(
    Guid Id,
    Guid ChapterId,
    ChapterReviewAction Action,
    Guid ActorUserId,
    string? Comment,
    DateTime? ScheduledAt,
    long ChapterVersion,
    DateTime CreatedAt)
{
    public static ChapterReviewDto From(ChapterReview r) =>
        new(r.Id, r.ChapterId, r.Action, r.ActorUserId, r.Comment, r.ScheduledAt, r.ChapterVersion, r.CreatedAt);
}

public sealed record ReopenChapterRequest(string? Comment);
public sealed record PublishChapterRequest(DateTime? PublishAt);
