using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Commands.ReturnWorkFromReview;

/// <summary>
/// Màn duyệt việc cấp Chapter (vd 7. Review): Editor thấy lỗi ở một trang thì trả đúng việc của trang/khung đó về người làm
/// (vd 6. Letter của Trang 2), kèm mô tả và vùng khoanh. Việc đã duyệt → ReworkRequired, StageProgress → ChangesRequested,
/// các stage sau của cùng Panel làm lại theo (CF4 4.7). Việc Review vì thế không nộp/duyệt được tới khi trang xong lại
/// (StageProgressRules.ChapterWorkBlockReasonAsync). Việc đang chờ duyệt thì phải trả qua cổng duyệt CF3 như thường.
/// </summary>
public sealed record ReturnWorkFromReviewCommand(
    Guid ChapterId,
    Guid ReviewAssignmentId,
    Guid TargetAssignmentId,
    Guid UserId,
    string Description,
    string? RegionJson = null,
    Guid? PanelId = null,
    Guid? AssetReferenceId = null,
    DateTime? DueDate = null) : IRequest<Result<ChangeRequestDto>>;

public sealed class ReturnWorkFromReviewCommandHandler : IRequestHandler<ReturnWorkFromReviewCommand, Result<ChangeRequestDto>>
{
    private readonly IPanelForgeDbContext _db;

    public ReturnWorkFromReviewCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ChangeRequestDto>> Handle(ReturnWorkFromReviewCommand cmd, CancellationToken ct)
    {
        static Result<ChangeRequestDto> Fail(string m, string c) => Result<ChangeRequestDto>.Failure(m, c);

        if (string.IsNullOrWhiteSpace(cmd.Description))
            return Fail("Phải mô tả cần sửa gì (BR-06).", ResultErrorCodes.Validation);

        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null) return Fail("Chương không tồn tại.", ResultErrorCodes.NotFound);
        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked) return Fail(locked, ResultErrorCodes.Conflict);

        var review = await _db.Assignments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == cmd.ReviewAssignmentId && a.ChapterId == cmd.ChapterId, ct);
        if (review is null) return Fail("Việc duyệt chương không tồn tại.", ResultErrorCodes.NotFound);
        if (review.EntityType != WorkflowEntityType.Chapter)
            return Fail("Chỉ trả việc từ màn duyệt cả chương (việc cấp Chapter).", ResultErrorCodes.Validation);
        if (review.Status == AssignmentStatus.Approved)
            return Fail("Việc duyệt chương đã được duyệt xong; mở lại chương để yêu cầu sửa.", ResultErrorCodes.Conflict);

        var target = await _db.Assignments
            .FirstOrDefaultAsync(a => a.Id == cmd.TargetAssignmentId && a.ChapterId == cmd.ChapterId, ct);
        if (target is null) return Fail("Việc cần trả về không thuộc chương này.", ResultErrorCodes.NotFound);
        if (target.EntityType == WorkflowEntityType.Chapter)
            return Fail("Chỉ trả về việc của trang hoặc khung.", ResultErrorCodes.Validation);
        if (target.Status is AssignmentStatus.Submitted or AssignmentStatus.UnderReview)
            return Fail("Việc này đang chờ duyệt: hãy trả về qua cổng duyệt của chính việc đó.", ResultErrorCodes.Conflict);

        // Khung khoanh phải thuộc đúng việc được trả về
        if (cmd.PanelId is { } panelId &&
            !(await StageProgressRules.ResolvePanelsAsync(_db, target, ct)).Any(p => p.Id == panelId))
            return Fail("Khung được khoanh không thuộc việc được trả về.", ResultErrorCodes.Validation);

        var description = cmd.Description.Trim();
        if (target.Status == AssignmentStatus.Approved)
        {
            if (await StageProgressRules.TransitionAsync(_db, target, r => r.ReopenForRework(), ct) is { } error)
                return Fail(error, ResultErrorCodes.Conflict);
            target.RequestRework(description);
            target.UpdatedBy = cmd.UserId.ToString();

            var (_, laterError) = await StageProgressRules.InvalidateLaterStagesAsync(_db, target, description, ct);
            if (laterError is not null) return Fail(laterError, ResultErrorCodes.Conflict);
        }

        ChangeRequest cr;
        try
        {
            cr = ChangeRequest.Create(target.Id, chapter.Id, null, target.AssigneeUserId, cmd.UserId, description,
                cmd.RegionJson, cmd.DueDate, cmd.AssetReferenceId, cmd.PanelId);
        }
        catch (ArgumentException ex)
        {
            return Fail(ex.Message, ResultErrorCodes.Validation);
        }

        _db.ChangeRequests.Add(cr);
        _db.Notifications.Add(Notification.Create(target.AssigneeUserId, NotificationType.AssignmentChangesRequested,
            "Có yêu cầu sửa từ bước duyệt chương", description, target.Id, target.ChapterId));

        await _db.SaveChangesAsync(ct);
        return Result<ChangeRequestDto>.Success(ChangeRequestDto.From(cr));
    }
}
