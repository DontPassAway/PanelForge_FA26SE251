using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Commands.HandOffChapter;

/// <summary>
/// Producer bàn giao chương đã nghiệm thu (Gate 3) sang cho Publisher để phát hành (POST /api/chapters/{id}/hand-off).
/// </summary>
public sealed record HandOffChapterCommand(Guid ChapterId, Guid ProducerUserId) : IRequest<Result<ChapterReviewDto>>;

public sealed class HandOffChapterCommandHandler : IRequestHandler<HandOffChapterCommand, Result<ChapterReviewDto>>
{
    private readonly IPanelForgeDbContext _db;

    public HandOffChapterCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ChapterReviewDto>> Handle(HandOffChapterCommand cmd, CancellationToken ct)
    {
        var loaded = await ChapterReviewRules.LoadAsync(_db, cmd.ChapterId, ChapterStatus.ChapterApproved, ct);
        if (!loaded.IsSuccess)
            return Result<ChapterReviewDto>.Failure(loaded.ErrorMessage!, loaded.ErrorCode);

        var (chapter, workspaceId) = loaded.Value;

        if (chapter.IsHandedOff)
            return Result<ChapterReviewDto>.Failure("Chương đã được bàn giao trước đó.", ResultErrorCodes.Conflict);

        chapter.HandOff(cmd.ProducerUserId);
        chapter.UpdatedBy = cmd.ProducerUserId.ToString();

        var review = ChapterReview.Create(
            chapter.Id,
            ChapterReviewAction.ProducerHandedOff,
            cmd.ProducerUserId,
            chapter.VersionVector,
            comment: "Producer bàn giao chương cho Publisher");
        _db.ChapterReviews.Add(review);

        var systemPublisherIds = await _db.Users.AsNoTracking()
            .Where(u => u.Role == SystemRole.Publisher && u.IsActive && u.Id != cmd.ProducerUserId)
            .Select(u => u.Id)
            .ToListAsync(ct);
        var workspacePublisherIds = await ChapterReviewRules.MemberIdsAsync(_db, workspaceId, WorkspaceRole.Publisher, cmd.ProducerUserId, ct);
        var allPublishers = systemPublisherIds.Union(workspacePublisherIds).Distinct();

        // Quyền phát hành xét vai trò trong Series: Publisher của Workspace chưa vào Series thì nhận thông báo mà bấm
        // Phát hành bị 403. Bàn giao là lúc Producer giao chương cho họ, nên thêm họ vào Series với vai trò Publisher.
        var seriesMemberIds = await _db.SeriesMembers.Where(m => m.SeriesId == chapter.SeriesId).Select(m => m.UserId).ToListAsync(ct);
        foreach (var publisherId in workspacePublisherIds.Except(seriesMemberIds))
            _db.SeriesMembers.Add(SeriesMember.Create(chapter.SeriesId, publisherId, WorkspaceRole.Publisher));

        foreach (var publisherId in allPublishers)
        {
            _db.Notifications.Add(ChapterReviewRules.Notify(
                publisherId,
                NotificationType.ChapterHandedOff,
                "Producer đã bàn giao chương để phát hành",
                chapter));
        }

        await _db.SaveChangesAsync(ct);
        return Result<ChapterReviewDto>.Success(ChapterReviewDto.From(review));
    }
}
