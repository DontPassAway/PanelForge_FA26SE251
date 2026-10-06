using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Commands.StartAssignment;

/// <summary>CF2: người được giao bắt đầu làm. BR-19: chỉ bắt đầu khi việc mà nó phụ thuộc đã Approved; việc khác không bị ảnh hưởng.</summary>
public sealed record StartAssignmentCommand(Guid ChapterId, Guid AssignmentId, Guid UserId)
    : IRequest<Result<AssignmentDto>>;

public sealed class StartAssignmentCommandHandler : IRequestHandler<StartAssignmentCommand, Result<AssignmentDto>>
{
    private readonly IPanelForgeDbContext _db;

    public StartAssignmentCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<AssignmentDto>> Handle(StartAssignmentCommand cmd, CancellationToken ct)
    {
        static Result<AssignmentDto> Fail(string msg, string code) => Result<AssignmentDto>.Failure(msg, code);

        var assignment = await _db.Assignments
            .FirstOrDefaultAsync(a => a.Id == cmd.AssignmentId && a.ChapterId == cmd.ChapterId, ct);
        if (assignment is null)
            return Fail($"Assignment {cmd.AssignmentId} không tồn tại.", ResultErrorCodes.NotFound);

        if (assignment.AssigneeUserId != cmd.UserId)
            return Fail("Chỉ người được giao mới bắt đầu được việc này.", ResultErrorCodes.Forbidden);

        var chapter = await _db.Chapters.AsNoTracking().FirstAsync(c => c.Id == cmd.ChapterId, ct);
        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked)
            return Fail(locked, ResultErrorCodes.Conflict);

        // BR-14: việc chỉ có hiệu lực sau khi người nhận đã được thông báo
        var notified = await _db.Notifications.AnyAsync(n =>
            n.AssignmentId == assignment.Id && n.UserId == assignment.AssigneeUserId &&
            n.Type == NotificationType.AssignmentAssigned, ct);
        if (!notified)
            return Fail("Người nhận chưa được thông báo về việc này nên việc chưa có hiệu lực (BR-14).", ResultErrorCodes.Conflict);

        if (assignment.DependsOnAssignmentId is { } depId)
        {
            var depStatus = await _db.Assignments.AsNoTracking()
                .Where(a => a.Id == depId).Select(a => (AssignmentStatus?)a.Status).FirstOrDefaultAsync(ct);
            if (depStatus != AssignmentStatus.Approved)
                return Fail("Việc phụ thuộc chưa được duyệt, chưa thể bắt đầu.", ResultErrorCodes.Conflict);
        }

        var stage = await _db.PipelineStages.AsNoTracking().FirstAsync(s => s.Id == assignment.PipelineStageId, ct);
        var panels = await StageProgressRules.ResolvePanelsAsync(_db, assignment, ct);
        if (await StageProgressRules.PriorStageBlockReasonAsync(_db, assignment, stage, panels, ct) is { } blocked)
            return Fail(blocked, ResultErrorCodes.Conflict);

        try { assignment.StartWork(); }
        catch (InvalidOperationException ex) { return Fail(ex.Message, ResultErrorCodes.Conflict); }

        if (await StageProgressRules.ApplyAsync(_db, stage, panels, cmd.UserId, submit: false, ct) is { } progressError)
            return Fail(progressError, ResultErrorCodes.Conflict);

        assignment.UpdatedBy = cmd.UserId.ToString();
        await _db.SaveChangesAsync(ct);
        return Result<AssignmentDto>.Success(AssignmentDto.From(assignment));
    }
}
