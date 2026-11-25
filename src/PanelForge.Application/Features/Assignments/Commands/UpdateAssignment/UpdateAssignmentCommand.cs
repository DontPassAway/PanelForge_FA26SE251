using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Commands.UpdateAssignment;

/// <summary>UC-05: sửa brief/hạn/ưu tiên, giao lại người nhận, đổi dependency. PUT thay toàn bộ.</summary>
public sealed record UpdateAssignmentCommand(
    Guid ChapterId,
    Guid AssignmentId,
    Guid UserId,
    string Brief,
    string? ReferenceNotes,
    DateTime? DueDate,
    AssignmentPriority Priority,
    Guid AssigneeUserId,
    Guid? DependsOnAssignmentId,
    IReadOnlyList<Guid>? DependsOnAssignmentIds = null
) : IRequest<Result<AssignmentDto>>;

public sealed class UpdateAssignmentCommandHandler : IRequestHandler<UpdateAssignmentCommand, Result<AssignmentDto>>
{
    private static readonly AssignmentStatus[] EditableStatuses =
        [AssignmentStatus.Assigned, AssignmentStatus.InProgress, AssignmentStatus.ReworkRequired, AssignmentStatus.Blocked];

    private readonly IPanelForgeDbContext _db;

    public UpdateAssignmentCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<AssignmentDto>> Handle(UpdateAssignmentCommand cmd, CancellationToken ct)
    {
        static Result<AssignmentDto> Fail(string msg, string? code = ResultErrorCodes.Validation) =>
            Result<AssignmentDto>.Failure(msg, code);

        if (string.IsNullOrWhiteSpace(cmd.Brief))
            return Fail("Brief không được để trống.");

        // Lọc theo ChapterId của route để chặn IDOR giữa các Workspace
        var assignment = await _db.Assignments
            .FirstOrDefaultAsync(a => a.Id == cmd.AssignmentId && a.ChapterId == cmd.ChapterId, ct);
        if (assignment is null)
            return Fail($"Assignment {cmd.AssignmentId} không tồn tại.", ResultErrorCodes.NotFound);

        var chapter = await _db.Chapters.AsNoTracking().FirstAsync(c => c.Id == cmd.ChapterId, ct);
        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked)
            return Fail(locked, ResultErrorCodes.Conflict);

        if (!EditableStatuses.Contains(assignment.Status))
            return Fail(assignment.Status switch
            {
                // Lượt nộp đang chờ thuộc về người đã nộp: không đổi người dưới nó, để nó không bị duyệt nhầm thành bài của người mới
                AssignmentStatus.Submitted or AssignmentStatus.UnderReview =>
                    "Việc đang có lượt nộp chờ duyệt: Editor cần duyệt hoặc trả sửa lượt nộp đó trước khi sửa hay đổi người.",
                AssignmentStatus.Approved =>
                    "Việc đã được duyệt: phải mở lại hợp lệ (trả về từ màn duyệt chương hoặc từ phản hồi xem thử) trước khi sửa hay đổi người.",
                _ => $"Không thể sửa Assignment đang ở trạng thái {assignment.Status}."
            }, ResultErrorCodes.Conflict);

        var depIds = cmd.DependsOnAssignmentIds ?? (cmd.DependsOnAssignmentId.HasValue ? [cmd.DependsOnAssignmentId.Value] : null);
        if (await AssignmentRules.ValidateDependenciesAsync(
                _db, cmd.ChapterId, assignment.Id, depIds, ct) is { } depError)
            return Fail(depError);

        var previousAssignee = assignment.AssigneeUserId;
        var reassigned = cmd.AssigneeUserId != previousAssignee;
        var detailsChanged = assignment.Brief != cmd.Brief.Trim()
                          || assignment.ReferenceNotes != cmd.ReferenceNotes?.Trim()
                          || assignment.DueDate != cmd.DueDate
                          || assignment.Priority != cmd.Priority
                          || !assignment.GetDependsOnAssignmentIds().SequenceEqual(depIds ?? []);

        if (reassigned)
        {
            var workspaceId = await _db.Series.AsNoTracking()
                .Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstOrDefaultAsync(ct);
            var stage = await _db.PipelineStages.AsNoTracking().FirstAsync(s => s.Id == assignment.PipelineStageId, ct);

            var role = await AssignmentRules.ResolveAssigneeRoleAsync(_db, workspaceId, cmd.AssigneeUserId, stage, ct);
            if (!role.IsSuccess) return Fail(role.ErrorMessage!, role.ErrorCode);

            assignment.Reassign(cmd.AssigneeUserId, role.Value);
            await AssignmentRules.EnsureSeriesMemberAsync(_db, chapter.SeriesId, cmd.AssigneeUserId, role.Value, ct);
            // Đổi người khi bị trả sửa: yêu cầu sửa còn mở đi theo người mới (người cũ không còn sửa được phần này)
            await ChangeRequestRules.TransferOpenAsync(_db, assignment, assignment, null, ct);
        }

        assignment.UpdateBriefAndDeadline(cmd.Brief, cmd.ReferenceNotes, null, cmd.DueDate, cmd.Priority);
        assignment.SetDependencies(depIds);
        assignment.UpdatedBy = cmd.UserId.ToString();

        // BR-14 / UC-14: báo người nhận mới (và người cũ) khi giao lại; báo người đang giữ khi nội dung đổi
        if (reassigned)
        {
            _db.Notifications.Add(AssignmentNotifications.Unassigned(assignment, previousAssignee));
            _db.Notifications.Add(AssignmentNotifications.Assigned(assignment));
        }
        else if (detailsChanged)
        {
            _db.Notifications.Add(AssignmentNotifications.Updated(assignment));
        }

        await _db.SaveChangesAsync(ct);
        return Result<AssignmentDto>.Success(AssignmentDto.From(assignment));
    }
}
