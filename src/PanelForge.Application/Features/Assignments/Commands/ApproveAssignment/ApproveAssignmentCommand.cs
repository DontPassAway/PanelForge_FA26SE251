using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Commands.ApproveAssignment;

/// <summary>
/// CF3 bước 6 (BR-06, BR-19): duyệt cổng. Assignment UnderReview → Approved, StageProgress các Panel InReview → Approved,
/// nhờ đó stage kế tiếp của các Panel đó mở được. Người duyệt phải khác người nhận.
/// </summary>
public sealed record ApproveAssignmentCommand(Guid ChapterId, Guid AssignmentId, Guid UserId, Guid? ExpectedSubmissionId = null)
    : IRequest<Result<AssignmentDto>>;

public sealed class ApproveAssignmentCommandHandler : IRequestHandler<ApproveAssignmentCommand, Result<AssignmentDto>>
{
    private readonly IPanelForgeDbContext _db;

    public ApproveAssignmentCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<AssignmentDto>> Handle(ApproveAssignmentCommand cmd, CancellationToken ct)
    {
        var loaded = await ReviewRules.LoadAsync(_db, cmd.ChapterId, cmd.AssignmentId, cmd.UserId, AssignmentStatus.UnderReview, ct);
        if (!loaded.IsSuccess)
            return Result<AssignmentDto>.Failure(loaded.ErrorMessage!, loaded.ErrorCode);

        var assignment = loaded.Value!;

        if (await ChangeRequestRules.SubmissionMismatchAsync(_db, assignment.Id, cmd.ExpectedSubmissionId, ct) is { } stale)
            return Result<AssignmentDto>.Failure(stale, ResultErrorCodes.Conflict);

        // CF3 "Kết quả": cổng chỉ đạt khi mọi yêu cầu sửa đã đóng; Editor phải xác minh các yêu cầu Resolved trước
        var unclosed = await ChangeRequestRules.UnclosedCountAsync(_db, assignment.Id, includeResolved: true, ct);
        if (unclosed > 0)
            return Result<AssignmentDto>.Failure(
                $"Còn {unclosed} yêu cầu sửa chưa đóng (Open, hoặc Resolved chưa xác minh), chưa thể duyệt.", ResultErrorCodes.Conflict);

        if (await StageProgressRules.TransitionAsync(_db, assignment, r => r.Approve(), ct) is { } error)
            return Result<AssignmentDto>.Failure(error, ResultErrorCodes.Conflict);

        (await ChangeRequestRules.LatestPendingSubmissionAsync(_db, assignment.Id, ct))?.Approve(cmd.UserId);
        assignment.MarkApproved();
        assignment.UpdatedBy = cmd.UserId.ToString();
        _db.Notifications.Add(AssignmentNotifications.Approved(assignment));

        await _db.SaveChangesAsync(ct);
        return Result<AssignmentDto>.Success(AssignmentDto.From(assignment));
    }
}
