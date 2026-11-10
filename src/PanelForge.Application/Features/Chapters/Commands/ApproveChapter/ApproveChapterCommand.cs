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
/// Kèm theo: tạo ChapterSnapshot (CF4 / 4.1).
/// Điều kiện: có ít nhất một Page, mọi việc giao đã Approved, không còn yêu cầu sửa Open/Resolved, không còn lỗi nhất quán Blocking đang mở. Từ đây việc/tiến độ bị khoá (BR-12).
/// </summary>
public sealed record ApproveChapterCommand(Guid ChapterId, Guid UserId) : IRequest<Result<ChapterReviewDto>>;

public sealed class ApproveChapterCommandHandler : IRequestHandler<ApproveChapterCommand, Result<ChapterReviewDto>>
{
    private readonly IPanelForgeDbContext _db;

    private readonly IPageRepository _pages;

    public ApproveChapterCommandHandler(IPanelForgeDbContext db, IPageRepository pages)
    {
        _db = db;
        _pages = pages;
    }

    public async Task<Result<ChapterReviewDto>> Handle(ApproveChapterCommand cmd, CancellationToken ct)
    {
        var loaded = await ChapterReviewRules.LoadAsync(_db, cmd.ChapterId, ChapterStatus.InProduction, ct);
        if (!loaded.IsSuccess)
            return Result<ChapterReviewDto>.Failure(loaded.ErrorMessage!, loaded.ErrorCode);

        var (chapter, workspaceId) = loaded.Value;

        // Cùng bộ luật với GET /api/chapters/{id}/lock-gate (ChapterLockGate): chặn ở điều kiện Blocking đầu tiên
        var blocker = (await ChapterLockGate.EvaluateAsync(_db, chapter, ct)).FirstOrDefault(i => i.Blocking);
        if (blocker is not null)
            return Result<ChapterReviewDto>.Failure(blocker.Message, ResultErrorCodes.Conflict);

        chapter.Approve();
        chapter.UpdatedBy = cmd.UserId.ToString();

        // CF4 / 4.1: manifest bất biến của bản vừa duyệt, lưu cùng SaveChanges với Approve (BR-18);
        // duyệt lại sau khi mở lại tạo snapshot mới, bản cũ giữ nguyên (BR-01)
        _db.ChapterSnapshots.Add(await ChapterSnapshotBuilder.CreateAsync(_db, _pages, chapter, cmd.UserId, ct));

        var review = ChapterReview.Create(chapter.Id, ChapterReviewAction.ProducerApproved, cmd.UserId, chapter.VersionVector);
        _db.ChapterReviews.Add(review);

        var systemPublisherIds = await _db.Users.AsNoTracking()
            .Where(u => u.Role == SystemRole.Publisher && u.IsActive && !u.IsDeleted && u.Id != cmd.UserId)
            .Select(u => u.Id)
            .ToListAsync(ct);
        var workspacePublisherIds = await ChapterReviewRules.MemberIdsAsync(_db, workspaceId, WorkspaceRole.Publisher, cmd.UserId, ct);
        var allPublishers = systemPublisherIds.Union(workspacePublisherIds).Distinct();

        foreach (var publisherId in allPublishers)
            _db.Notifications.Add(ChapterReviewRules.Notify(
                publisherId, NotificationType.ChapterReadyForPublication, "Có chương chờ duyệt xuất bản", chapter));

        await _db.SaveChangesAsync(ct);
        return Result<ChapterReviewDto>.Success(ChapterReviewDto.From(review));
    }
}
