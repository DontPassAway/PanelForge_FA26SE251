using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Commands.DeleteAssignment;

/// <summary>Producer xoá mềm / huỷ Assignment khỏi Chapter (UC-05). Giải phóng các dependency phụ thuộc vào nó.</summary>
public sealed record DeleteAssignmentCommand(
    Guid ChapterId,
    Guid AssignmentId,
    Guid UserId
) : IRequest<Result<bool>>;

public sealed class DeleteAssignmentCommandHandler : IRequestHandler<DeleteAssignmentCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _db;

    public DeleteAssignmentCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<bool>> Handle(DeleteAssignmentCommand cmd, CancellationToken ct)
    {
        static Result<bool> Fail(string msg, string? code = ResultErrorCodes.Validation) =>
            Result<bool>.Failure(msg, code);

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

        // Giải phóng các Assignment khác trong cùng Chapter đang phụ thuộc vào Assignment này
        var dependentAssignments = await _db.Assignments
            .Where(a => a.ChapterId == cmd.ChapterId && a.DependsOnAssignmentId == assignment.Id)
            .ToListAsync(ct);

        foreach (var dep in dependentAssignments)
        {
            dep.SetDependency(null);
        }

        // Gửi thông báo cho người đang nhận việc nếu có người nhận và người nhận khác người thực hiện xoá
        if (assignment.AssigneeUserId != Guid.Empty && assignment.AssigneeUserId != cmd.UserId)
        {
            var notification = Notification.Create(
                assignment.AssigneeUserId,
                NotificationType.AssignmentUnassigned,
                "Nhiệm vụ phân công đã bị huỷ",
                $"Nhiệm vụ '{assignment.Brief}' đã bị Producer huỷ khỏi chương.",
                assignment.Id,
                assignment.ChapterId);
            _db.Notifications.Add(notification);
        }

        // DbContext tự động chuyển Remove thành soft-delete (ISoftDelete: IsDeleted = true, DeletedAt, DeletedBy)
        _db.Assignments.Remove(assignment);

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}
