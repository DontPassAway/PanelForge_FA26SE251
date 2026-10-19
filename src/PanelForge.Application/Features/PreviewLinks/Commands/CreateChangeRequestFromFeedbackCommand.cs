using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.PreviewLinks.Models;
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

        var assignment = await _db.Assignments.FirstOrDefaultAsync(a => a.Id == cmd.AssignmentId && a.ChapterId == cmd.ChapterId, ct);
        if (assignment is null) return Fail("Việc được chọn không thuộc chương này.", ResultErrorCodes.NotFound);
        if (assignment.Status is AssignmentStatus.Submitted or AssignmentStatus.UnderReview)
            return Fail("Việc đang chờ duyệt: hãy trả về qua cổng duyệt (CF3) kèm yêu cầu sửa.", ResultErrorCodes.Conflict);

        var description = string.IsNullOrWhiteSpace(cmd.Description)
            ? $"Phản hồi xem thử từ {feedback.RecipientEmail}: {feedback.Message}" +
              (feedback.TriageNote is { } note ? $"\nGhi chú triage: {note}" : "")
            : cmd.Description.Trim();

        // Việc đã duyệt: mở lại stage đã đóng của các Panel việc bao phủ để người làm Start lại được
        if (assignment.Status == AssignmentStatus.Approved)
        {
            if (await StageProgressRules.TransitionAsync(_db, assignment, r => r.ReopenForRework(), ct) is { } error)
                return Fail(error, ResultErrorCodes.Conflict);
            assignment.RequestRework(description);
            assignment.UpdatedBy = cmd.UserId.ToString();

            // CF4 4.7: artwork stage này sẽ đổi nên việc ở các stage sau của cùng Panel cũng phải làm lại
            var (_, laterError) = await StageProgressRules.InvalidateLaterStagesAsync(_db, assignment, description, ct);
            if (laterError is not null)
                return Fail(laterError, ResultErrorCodes.Conflict);
        }

        var cr = ChangeRequest.Create(assignment.Id, chapter.Id, null, assignment.AssigneeUserId, cmd.UserId,
            description, dueDate: cmd.DueDate, assetReferenceId: feedback.AssetReferenceId, panelId: feedback.PanelId);
        _db.ChangeRequests.Add(cr);
        feedback.LinkChangeRequest(cr.Id);
        feedback.UpdatedBy = cmd.UserId.ToString();
        _db.Notifications.Add(Notification.Create(assignment.AssigneeUserId, NotificationType.AssignmentChangesRequested,
            "Có yêu cầu sửa từ phản hồi xem thử", description, assignment.Id, assignment.ChapterId));

        await _db.SaveChangesAsync(ct);
        return Result<PreviewFeedbackDto>.Success(PreviewFeedbackDto.From(feedback));
    }
}
