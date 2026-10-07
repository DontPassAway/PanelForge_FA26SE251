using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Commands.ApproveChapter;

/// <summary>
/// Họp tuần 5 / CF5 bước 1–2 (UC-20, BR-12): Producer duyệt bản hoàn chỉnh. Chapter InProduction → Approved, chuyển cho Publisher.
/// Điều kiện: có ít nhất một Page, mọi việc giao đã Approved, không còn yêu cầu sửa Open/Resolved. Từ đây việc/tiến độ bị khoá (BR-12).
/// </summary>
public sealed record ApproveChapterCommand(Guid ChapterId, Guid UserId) : IRequest<Result<ChapterReviewDto>>;

public sealed class ApproveChapterCommandHandler : IRequestHandler<ApproveChapterCommand, Result<ChapterReviewDto>>
{
    private readonly IPanelForgeDbContext _db;

    public ApproveChapterCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ChapterReviewDto>> Handle(ApproveChapterCommand cmd, CancellationToken ct)
    {
        var loaded = await ChapterReviewRules.LoadAsync(_db, cmd.ChapterId, ChapterStatus.InProduction, ct);
        if (!loaded.IsSuccess)
            return Result<ChapterReviewDto>.Failure(loaded.ErrorMessage!, loaded.ErrorCode);

        var (chapter, workspaceId) = loaded.Value;

        if (!await _db.Pages.AnyAsync(p => p.ChapterId == chapter.Id, ct))
            return Result<ChapterReviewDto>.Failure("Chapter chưa có Page nào, chưa thể duyệt.", ResultErrorCodes.Conflict);

        var unfinished = await _db.Assignments.CountAsync(a => a.ChapterId == chapter.Id && a.Status != AssignmentStatus.Approved, ct);
        if (unfinished > 0)
            return Result<ChapterReviewDto>.Failure($"Còn {unfinished} việc chưa được duyệt, chưa thể duyệt Chapter.", ResultErrorCodes.Conflict);

        var openRequests = await _db.ChangeRequests.CountAsync(c => c.ChapterId == chapter.Id &&
            (c.Status == ChangeRequestStatus.Open || c.Status == ChangeRequestStatus.Resolved), ct);
        if (openRequests > 0)
            return Result<ChapterReviewDto>.Failure($"Còn {openRequests} yêu cầu sửa chưa đóng, chưa thể duyệt Chapter.", ResultErrorCodes.Conflict);

        chapter.Approve();
        chapter.UpdatedBy = cmd.UserId.ToString();

        var review = ChapterReview.Create(chapter.Id, ChapterReviewAction.ProducerApproved, cmd.UserId, chapter.VersionVector);
        _db.ChapterReviews.Add(review);

        foreach (var publisherId in await ChapterReviewRules.MemberIdsAsync(_db, workspaceId, WorkspaceRole.Publisher, cmd.UserId, ct))
            _db.Notifications.Add(ChapterReviewRules.Notify(
                publisherId, NotificationType.ChapterReadyForPublication, "Có chương chờ duyệt xuất bản", chapter));

        await _db.SaveChangesAsync(ct);
        return Result<ChapterReviewDto>.Success(ChapterReviewDto.From(review));
    }
}
