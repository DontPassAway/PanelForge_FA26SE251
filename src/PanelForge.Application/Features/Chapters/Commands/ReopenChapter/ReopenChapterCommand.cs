using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Commands.ReopenChapter;

/// <summary>
/// CF5 bước 7 (UC-23): trả chương Approved về InProduction kèm lý do bắt buộc. Producer tự mở lại, hoặc Publisher từ chối
/// xuất bản (khi đó Producer được báo). Chương đã Published là bản cuối nên không mở lại được.
/// </summary>
public sealed record ReopenChapterCommand(Guid ChapterId, Guid UserId, string? Comment) : IRequest<Result<ChapterReviewDto>>;

public sealed class ReopenChapterCommandHandler : IRequestHandler<ReopenChapterCommand, Result<ChapterReviewDto>>
{
    private readonly IPanelForgeDbContext _db;

    public ReopenChapterCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ChapterReviewDto>> Handle(ReopenChapterCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Comment))
            return Result<ChapterReviewDto>.Failure("Mở lại / từ chối bắt buộc có lý do.", ResultErrorCodes.Validation);

        var loaded = await ChapterReviewRules.LoadAsync(_db, cmd.ChapterId, ChapterStatus.Approved, ct);
        if (!loaded.IsSuccess)
            return Result<ChapterReviewDto>.Failure(loaded.ErrorMessage!, loaded.ErrorCode);

        var (chapter, workspaceId) = loaded.Value;

        var role = await _db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && m.UserId == cmd.UserId)
            .Select(m => (WorkspaceRole?)m.Role).FirstOrDefaultAsync(ct);
        var byPublisher = role == WorkspaceRole.Publisher;

        chapter.Reopen();
        chapter.UpdatedBy = cmd.UserId.ToString();

        // CF5: mở lại thì link xem thử của bản cũ bị thu hồi (BR-17)
        foreach (var link in await _db.PreviewLinks.Where(l => l.ChapterId == chapter.Id && l.RevokedAt == null).ToListAsync(ct))
            link.Revoke(cmd.UserId, "Chương được mở lại");

        var review = ChapterReview.Create(
            chapter.Id,
            byPublisher ? ChapterReviewAction.PublisherRejected : ChapterReviewAction.ProducerReopened,
            cmd.UserId, chapter.VersionVector, cmd.Comment);
        _db.ChapterReviews.Add(review);

        foreach (var producerId in await ChapterReviewRules.MemberIdsAsync(_db, workspaceId, WorkspaceRole.Producer, cmd.UserId, ct))
            _db.Notifications.Add(ChapterReviewRules.Notify(
                producerId, NotificationType.ChapterReopened,
                byPublisher ? "Publisher trả chương về sản xuất" : "Chương được mở lại để sửa", chapter, review.Comment));

        await _db.SaveChangesAsync(ct);
        return Result<ChapterReviewDto>.Success(ChapterReviewDto.From(review));
    }
}
