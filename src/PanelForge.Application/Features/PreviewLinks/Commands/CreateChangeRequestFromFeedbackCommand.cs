using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.PreviewLinks.Common;
using PanelForge.Application.Features.PreviewLinks.Models;
using PanelForge.Application.Features.TaskGrid.Commands.ReopenStageTask;
using PanelForge.Domain.Entities.Preview;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.PreviewLinks.Commands;

/// <summary>
/// CF5 bước 7 / CF3 (việc 5.7, 3.7): sau khi chương được mở lại, Producer/Editor chuyển một phản hồi "cần sửa" thành
/// ChangeRequest của một việc trong chương. Việc đã duyệt bị trả về làm lại (Approved → ReworkRequired, StageProgress
/// Approved → ChangesRequested); việc chưa duyệt chỉ nhận thêm yêu cầu sửa. Người làm được báo (BR-14). Chương chỉ duyệt
/// lại được khi yêu cầu đã được xác minh (ApproveChapter chặn ChangeRequest Open/Resolved).
/// CF4 4.7: việc ở các stage sau của cùng Panel cũng bị trả về theo (xem StageProgressRules.InvalidateLaterStagesAsync).
/// </summary>
public sealed record CreateChangeRequestFromFeedbackCommand(
    Guid ChapterId, Guid FeedbackId, Guid UserId, Guid AssignmentId, string? Description, DateTime? DueDate)
    : IRequest<Result<PreviewFeedbackDto>>;

public sealed class CreateChangeRequestFromFeedbackCommandHandler
    : IRequestHandler<CreateChangeRequestFromFeedbackCommand, Result<PreviewFeedbackDto>>
{
    private readonly IPanelForgeDbContext _db;

    public CreateChangeRequestFromFeedbackCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<PreviewFeedbackDto>> Handle(CreateChangeRequestFromFeedbackCommand cmd, CancellationToken ct)
    {
        static Result<PreviewFeedbackDto> Fail(string m, string c) => Result<PreviewFeedbackDto>.Failure(m, c);

        var feedback = await _db.PreviewFeedback.FirstOrDefaultAsync(f => f.Id == cmd.FeedbackId && f.ChapterId == cmd.ChapterId, ct);
        if (feedback is null) return Fail("Phản hồi không tồn tại.", ResultErrorCodes.NotFound);
        if (feedback.Status != PreviewFeedbackStatus.NeedsChange)
            return Fail("Chỉ phản hồi đã phân loại \"cần sửa\" mới chuyển thành yêu cầu sửa.", ResultErrorCodes.Conflict);
        if (feedback.ChangeRequestId.HasValue)
            return Fail("Phản hồi này đã được chuyển thành yêu cầu sửa.", ResultErrorCodes.Conflict);

        var chapter = await _db.Chapters.AsNoTracking().FirstAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter.Status != ChapterStatus.InProduction)
            return Fail($"Chương đang ở trạng thái {chapter.Status}: mở lại chương (kèm lý do) trước khi tạo yêu cầu sửa.",
                ResultErrorCodes.Conflict);

        var description = string.IsNullOrWhiteSpace(cmd.Description)
            ? $"Phản hồi xem thử từ {feedback.RecipientEmail}: {feedback.Message}" +
              (feedback.TriageNote is { } note ? $"\nGhi chú triage: {note}" : "")
            : cmd.Description.Trim();

        var assignment = await _db.Assignments.FirstOrDefaultAsync(a => a.Id == cmd.AssignmentId && a.ChapterId == cmd.ChapterId, ct);
        if (assignment is null)
        {
            // Lưới V3: AssignmentId mang id của ô lưới
            var task = await _db.StageTasks.FirstOrDefaultAsync(t => t.Id == cmd.AssignmentId && t.ChapterId == cmd.ChapterId, ct);
            if (task is null) return Fail("Việc được chọn không thuộc chương này.", ResultErrorCodes.NotFound);
            return await FromGridAsync(cmd, feedback, task, description, ct);
        }
        if (assignment.Status is AssignmentStatus.Submitted or AssignmentStatus.UnderReview)
            return Fail("Việc đang chờ duyệt: hãy trả về qua cổng duyệt (CF3) kèm yêu cầu sửa.", ResultErrorCodes.Conflict);

        // Việc đã duyệt: mở lại stage đã đóng của các Panel việc bao phủ để người làm Start lại được
        if (assignment.Status == AssignmentStatus.Approved)
        {
            if (await StageProgressRules.TransitionAsync(_db, assignment, r => r.ReopenForRework(), ct) is { } error)
                return Fail(error, ResultErrorCodes.Conflict);
            assignment.RequestRework(description);
            assignment.UpdatedBy = cmd.UserId.ToString();

            // CF4 4.7: kết quả stage này sẽ đổi nên việc đã dựa vào nó được đánh dấu cần kiểm tra ảnh hưởng (không tự làm lại)
            await StageProgressRules.FlagImpactedWorkAsync(_db, assignment, description, ct);
        }

        var cr = ChangeRequest.Create(assignment.Id, chapter.Id, null, assignment.AssigneeUserId, cmd.UserId,
            description, dueDate: cmd.DueDate, assetReferenceId: feedback.AssetReferenceId, panelId: feedback.PanelId);
        _db.ChangeRequests.Add(cr);
        feedback.LinkChangeRequest(cr.Id);
        feedback.UpdatedBy = cmd.UserId.ToString();
        _db.Notifications.Add(Notification.Create(assignment.AssigneeUserId, NotificationType.AssignmentChangesRequested,
            "Có yêu cầu sửa từ phản hồi xem thử", description, assignment.Id, assignment.ChapterId));

        await _db.SaveChangesAsync(ct);
        return Result<PreviewFeedbackDto>.Success(await PreviewLinkRules.FeedbackDtoAsync(_db, feedback, ct));
    }

    /// <summary>
    /// Lưới V3: ô đã duyệt được mở lại (ReworkRequired, ô hạ nguồn đã bắt đầu mang cờ lỗi thời, người làm được báo) qua đúng
    /// luật của POST …/task-grid/tasks/{id}/reopen; ô đang làm chỉ nhận thông báo yêu cầu sửa. Phản hồi gắn với ô.
    /// </summary>
    private async Task<Result<PreviewFeedbackDto>> FromGridAsync(
        CreateChangeRequestFromFeedbackCommand cmd, PreviewFeedback feedback, StageTask task, string description, CancellationToken ct)
    {
        static Result<PreviewFeedbackDto> Fail(string m, string c) => Result<PreviewFeedbackDto>.Failure(m, c);

        switch (task.Status)
        {
            case StageTaskStatus.Submitted or StageTaskStatus.UnderReview:
                return Fail("Ô đang chờ duyệt: hãy trả về qua nút yêu cầu sửa trên lưới kèm nhận xét.", ResultErrorCodes.Conflict);
            case StageTaskStatus.Unassigned or StageTaskStatus.Cancelled:
                return Fail("Ô chưa có người làm, chưa chuyển phản hồi thành yêu cầu sửa được.", ResultErrorCodes.Conflict);
            case StageTaskStatus.Approved:
                var reopened = await new ReopenStageTaskCommandHandler(_db)
                    .Handle(new ReopenStageTaskCommand(cmd.ChapterId, task.Id, cmd.UserId, description), ct);
                if (!reopened.IsSuccess) return Fail(reopened.ErrorMessage!, reopened.ErrorCode!);
                break;
            default:
                if (task.LotId is { } lotId &&
                    await _db.StageTaskLots.AsNoTracking().Where(l => l.Id == lotId).Select(l => (Guid?)l.AssigneeUserId)
                        .FirstOrDefaultAsync(ct) is { } assignee)
                    _db.Notifications.Add(Notification.Create(assignee, NotificationType.AssignmentChangesRequested,
                        "Có yêu cầu sửa từ phản hồi xem thử", description, chapterId: cmd.ChapterId));
                break;
        }

        feedback.LinkStageTask(task.Id);
        feedback.UpdatedBy = cmd.UserId.ToString();
        await _db.SaveChangesAsync(ct);
        return Result<PreviewFeedbackDto>.Success(await PreviewLinkRules.FeedbackDtoAsync(_db, feedback, ct));
    }
}
