using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Commands.DeleteAssignment;

/// <summary>
/// Producer huỷ Assignment khỏi Chapter (UC-05): lý do bắt buộc, ghi vào việc rồi xoá mềm (giữ lịch sử, audit log). Việc bị huỷ
/// không bao giờ tính là Approved; không huỷ được khi còn việc phụ thuộc vào nó.
/// </summary>
public sealed record DeleteAssignmentCommand(
    Guid ChapterId,
    Guid AssignmentId,
    Guid UserId,
    string? Reason = null
) : IRequest<Result<bool>>;

public sealed class DeleteAssignmentCommandHandler : IRequestHandler<DeleteAssignmentCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _db;

    public DeleteAssignmentCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<bool>> Handle(DeleteAssignmentCommand cmd, CancellationToken ct)
    {
        static Result<bool> Fail(string msg, string? code = ResultErrorCodes.Validation) =>
            Result<bool>.Failure(msg, code);

        if (string.IsNullOrWhiteSpace(cmd.Reason))
            return Fail("Huỷ việc bắt buộc có lý do.");

        var assignment = await _db.Assignments
            .FirstOrDefaultAsync(a => a.Id == cmd.AssignmentId && a.ChapterId == cmd.ChapterId, ct);
        if (assignment is null)
            return Fail($"Assignment {cmd.AssignmentId} không tồn tại.", ResultErrorCodes.NotFound);

        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null)
            return Fail("Chapter không tồn tại.", ResultErrorCodes.NotFound);

        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked)
            return Fail(locked, ResultErrorCodes.Conflict);

        if (assignment.Status == AssignmentStatus.Approved)
            return Fail("Không thể xoá Assignment đã được phê duyệt hoàn thành (Approved).", ResultErrorCodes.Conflict);

        // Đã nộp ít nhất một lần thì giữ lại: lịch sử nộp/duyệt và tiến độ Panel đang trỏ vào việc này
        if (await _db.Submissions.AnyAsync(s => s.AssignmentId == assignment.Id, ct))
            return Fail("Không thể xoá việc đã có bài nộp; hãy đổi người nhận thay vì xoá.", ResultErrorCodes.Conflict);

        // Không âm thầm gỡ phụ thuộc: việc phía sau sẽ bắt đầu được mà không cần việc này xong
        var dependents = (await _db.Assignments.AsNoTracking()
                .Where(a => a.ChapterId == cmd.ChapterId && a.Id != assignment.Id)
                .ToListAsync(ct))
            .Count(a => a.GetDependsOnAssignmentIds().Contains(assignment.Id));
        if (dependents > 0)
            return Fail($"Không thể xoá: còn {dependents} việc phụ thuộc vào việc này. Gỡ phụ thuộc ở các việc đó trước.",
                ResultErrorCodes.Conflict);

        // Gửi thông báo cho người đang nhận việc nếu có người nhận và người nhận khác người thực hiện xoá
        if (assignment.AssigneeUserId != Guid.Empty && assignment.AssigneeUserId != cmd.UserId)
        {
            var notification = Notification.Create(
                assignment.AssigneeUserId,
                NotificationType.AssignmentUnassigned,
                "Nhiệm vụ phân công đã bị huỷ",
                $"Nhiệm vụ '{assignment.Brief}' đã bị Producer huỷ khỏi chương. Lý do: {cmd.Reason!.Trim()}",
                assignment.Id,
                assignment.ChapterId);
            _db.Notifications.Add(notification);
        }

        assignment.Cancel(cmd.Reason!);
        // DbContext tự động chuyển Remove thành soft-delete (ISoftDelete: IsDeleted = true, DeletedAt, DeletedBy)
        _db.Assignments.Remove(assignment);

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}
