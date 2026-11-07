using Microsoft.EntityFrameworkCore;
using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Domain.Entities.Workflow;
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

        var stage = await _db.PipelineStages.AsNoTracking().FirstAsync(s => s.Id == assignment.PipelineStageId, ct);
        if (await StageProgressRules.ChapterWorkBlockReasonAsync(_db, assignment, stage, ct) is { } chapterBlocked)
            return Result<AssignmentDto>.Failure(chapterBlocked, ResultErrorCodes.Conflict);
        // Đầu vào có thể đã đổi trong lúc chờ duyệt: không duyệt kết quả dựa trên đầu vào chưa hợp lệ
        var panels = await StageProgressRules.ResolvePanelsAsync(_db, assignment, ct);
        if (await StageProgressRules.InputBlockReasonAsync(_db, assignment, stage, panels, ct) is { } inputBlocked)
            return Result<AssignmentDto>.Failure(inputBlocked, ResultErrorCodes.Conflict);

        if (await StageProgressRules.TransitionAsync(_db, assignment, r => r.Approve(), ct) is { } error)
            return Result<AssignmentDto>.Failure(error, ResultErrorCodes.Conflict);

        (await ChangeRequestRules.LatestPendingSubmissionAsync(_db, assignment.Id, ct))?.Approve(cmd.UserId);
        assignment.MarkApproved();
        assignment.UpdatedBy = cmd.UserId.ToString();
        _db.Notifications.Add(AssignmentNotifications.Approved(assignment));
        await NotifyReadyDependentsAsync(assignment, ct);

        await _db.SaveChangesAsync(ct);
        return Result<AssignmentDto>.Success(AssignmentDto.From(assignment));
    }

    /// <summary>
    /// Việc đang chờ (Assigned) phụ thuộc việc vừa duyệt: nếu mọi tiền đề của nó đã Approved thì báo người giữ "đến lượt bạn",
    /// gộp 1 thông báo mỗi người. Cùng lần Save với việc duyệt.
    /// </summary>
    private async Task NotifyReadyDependentsAsync(Assignment approved, CancellationToken ct)
    {
        // DependsOnAssignmentIdsJson là cột jsonb: không dịch string.Contains sang SQL (LIKE trên jsonb lỗi 42883),
        // nên lọc danh sách phụ thuộc trong bộ nhớ trên tập việc Assigned của một chương (nhỏ).
        var waiting = (await _db.Assignments.AsNoTracking()
                .Where(a => a.ChapterId == approved.ChapterId && a.CancelReason == null && a.Status == AssignmentStatus.Assigned)
                .ToListAsync(ct))
            .Where(a => a.DependsOnAssignmentId == approved.Id || a.GetDependsOnAssignmentIds().Contains(approved.Id))
            .ToList();
        if (waiting.Count == 0) return;

        var depIds = waiting.SelectMany(a => a.GetDependsOnAssignmentIds()).Distinct().Where(id => id != approved.Id).ToList();
        var approvedIds = (await _db.Assignments.AsNoTracking()
                .Where(a => depIds.Contains(a.Id) && a.Status == AssignmentStatus.Approved)
                .Select(a => a.Id).ToListAsync(ct))
            .Append(approved.Id).ToHashSet();

        foreach (var mine in waiting.Where(a => a.GetDependsOnAssignmentIds().All(approvedIds.Contains)).GroupBy(a => a.AssigneeUserId))
            _db.Notifications.Add(AssignmentNotifications.ReadyToStart(mine.OrderBy(a => a.CreatedAt).ToList()));
    }
}
