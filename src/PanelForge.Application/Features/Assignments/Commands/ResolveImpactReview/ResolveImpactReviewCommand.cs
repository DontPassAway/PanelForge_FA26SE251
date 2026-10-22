using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Commands.ResolveImpactReview;

/// <summary>
/// Quyết định cho việc "Cần kiểm tra ảnh hưởng" (đầu vào bị mở lại): <c>Keep</c> = kết quả vẫn hợp lệ với đầu vào mới, gỡ cờ;
/// <c>Rework</c> = phải làm lại: việc đã duyệt được mở lại hợp lệ (tiến độ Panel → ChangesRequested), mọi việc thành ReworkRequired
/// kèm yêu cầu sửa, và những việc dựa vào nó lại được đánh dấu. Lý do bắt buộc; quyết định nằm trong audit log, kết quả và quyết
/// định duyệt cũ không bị xoá. Việc đang có lượt nộp chờ duyệt thì làm lại qua cổng duyệt CF3, không qua đây.
/// </summary>
public sealed record ResolveImpactReviewCommand(Guid ChapterId, Guid AssignmentId, Guid UserId, ImpactReviewDecision Decision, string Reason)
    : IRequest<Result<AssignmentDto>>;

public enum ImpactReviewDecision
{
    Keep = 1,
    Rework = 2
}

public sealed class ResolveImpactReviewCommandHandler : IRequestHandler<ResolveImpactReviewCommand, Result<AssignmentDto>>
{
    private readonly IPanelForgeDbContext _db;

    public ResolveImpactReviewCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<AssignmentDto>> Handle(ResolveImpactReviewCommand cmd, CancellationToken ct)
    {
        static Result<AssignmentDto> Fail(string msg, string code) => Result<AssignmentDto>.Failure(msg, code);

        if (string.IsNullOrWhiteSpace(cmd.Reason))
            return Fail("Quyết định kiểm tra ảnh hưởng bắt buộc có lý do.", ResultErrorCodes.Validation);

        var assignment = await _db.Assignments
            .FirstOrDefaultAsync(a => a.Id == cmd.AssignmentId && a.ChapterId == cmd.ChapterId, ct);
        if (assignment is null)
            return Fail($"Assignment {cmd.AssignmentId} không tồn tại.", ResultErrorCodes.NotFound);
        if (assignment.AssigneeUserId == cmd.UserId)
            return Fail("Người làm không tự quyết định kiểm tra ảnh hưởng cho việc của mình.", ResultErrorCodes.Forbidden);

        var chapter = await _db.Chapters.AsNoTracking().FirstAsync(c => c.Id == cmd.ChapterId, ct);
        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked)
            return Fail(locked, ResultErrorCodes.Conflict);
        if (assignment.ImpactReviewReason is null)
            return Fail("Việc này không ở trạng thái cần kiểm tra ảnh hưởng.", ResultErrorCodes.Conflict);

        var reason = cmd.Reason.Trim();
        if (cmd.Decision == ImpactReviewDecision.Rework)
        {
            if (assignment.Status is AssignmentStatus.Submitted or AssignmentStatus.UnderReview)
                return Fail("Việc đang có lượt nộp chờ duyệt: trả sửa qua cổng duyệt của chính việc đó.", ResultErrorCodes.Conflict);

            if (assignment.Status == AssignmentStatus.Approved)
            {
                if (await StageProgressRules.TransitionAsync(_db, assignment, r =>
                    {
                        if (r.Status == StageProgressStatus.Approved) r.ReopenForRework();
                    }, ct) is { } error)
                    return Fail(error, ResultErrorCodes.Conflict);
            }

            var message = $"{assignment.ImpactReviewReason} — Cần làm lại: {reason}";
            assignment.RequestRework(message);
            _db.ChangeRequests.Add(ChangeRequest.Create(
                assignment.Id, assignment.ChapterId, null, assignment.AssigneeUserId, cmd.UserId, message));
            await StageProgressRules.FlagImpactedWorkAsync(_db, assignment, reason, ct);
            _db.Notifications.Add(AssignmentNotifications.ChangesRequested(assignment));
        }

        assignment.ClearImpactReview();
        assignment.UpdatedBy = cmd.UserId.ToString();
        await _db.SaveChangesAsync(ct);
        return Result<AssignmentDto>.Success(AssignmentDto.From(assignment));
    }
}
