using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Commands.PublishChapter;

/// <summary>
/// Họp tuần 5: Publisher kiểm tra và duyệt xuất bản chương đã được Producer duyệt, rồi lên lịch. Chapter Approved → Published;
/// <c>PublishedAt</c> = giờ lên lịch (bỏ trống = ngay), Reader chỉ thấy từ lúc đó. Published là bản cuối (BR-24).
/// </summary>
public sealed record PublishChapterCommand(Guid ChapterId, Guid UserId, DateTime? PublishAt) : IRequest<Result<ChapterReviewDto>>;

public sealed class PublishChapterCommandHandler : IRequestHandler<PublishChapterCommand, Result<ChapterReviewDto>>
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);

    private readonly IPanelForgeDbContext _db;

    public PublishChapterCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ChapterReviewDto>> Handle(PublishChapterCommand cmd, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var publishAt = cmd.PublishAt?.ToUniversalTime() ?? now;
        if (publishAt < now - ClockSkew)
            return Result<ChapterReviewDto>.Failure("Thời điểm phát hành không được ở quá khứ.", ResultErrorCodes.Validation);

        var loaded = await ChapterReviewRules.LoadAsync(_db, cmd.ChapterId, ChapterStatus.Approved, ct);
        if (!loaded.IsSuccess)
            return Result<ChapterReviewDto>.Failure(loaded.ErrorMessage!, loaded.ErrorCode);

        var (chapter, workspaceId) = loaded.Value;

        chapter.Publish(publishAt);
        chapter.UpdatedBy = cmd.UserId.ToString();

        var review = ChapterReview.Create(
            chapter.Id, ChapterReviewAction.PublisherPublished, cmd.UserId, chapter.VersionVector, scheduledAt: publishAt);
        _db.ChapterReviews.Add(review);

        foreach (var producerId in await ChapterReviewRules.MemberIdsAsync(_db, workspaceId, WorkspaceRole.Producer, cmd.UserId, ct))
            _db.Notifications.Add(ChapterReviewRules.Notify(
                producerId, NotificationType.ChapterPublished, "Publisher đã duyệt và lên lịch phát hành", chapter,
                $"phát hành lúc {publishAt:yyyy-MM-dd HH:mm} UTC"));

        await _db.SaveChangesAsync(ct);
        return Result<ChapterReviewDto>.Success(ChapterReviewDto.From(review));
    }
}
