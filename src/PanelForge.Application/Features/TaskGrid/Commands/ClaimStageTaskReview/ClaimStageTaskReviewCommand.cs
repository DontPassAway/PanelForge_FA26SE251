using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Application.Features.TaskGrid.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.TaskGrid.Commands.ClaimStageTaskReview;

/// <summary>
/// Editor mở duyệt (Open = true: Submitted → UnderReview, giữ khoá để Editor khác không duyệt đè) hoặc nhả khoá
/// (Open = false: UnderReview → Submitted, trả về hàng đợi). Nhả khoá: chính người đang duyệt hoặc Producer (gỡ khoá treo).
/// </summary>
public sealed record ClaimStageTaskReviewCommand(Guid ChapterId, Guid TaskId, Guid UserId, bool Open)
    : IRequest<Result<StageTaskDto>>, ITransactionalCommand;

public sealed class ClaimStageTaskReviewCommandHandler : IRequestHandler<ClaimStageTaskReviewCommand, Result<StageTaskDto>>
{
    private readonly IPanelForgeDbContext _db;
    public ClaimStageTaskReviewCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<StageTaskDto>> Handle(ClaimStageTaskReviewCommand cmd, CancellationToken ct)
    {
        static Result<StageTaskDto> Fail(string msg, string code) => Result<StageTaskDto>.Failure(msg, code);

        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null) return Fail($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked) return Fail(locked, ResultErrorCodes.Conflict);

        await _db.LockChapterAsync(chapter.Id, ct);

        var task = await _db.StageTasks.FirstOrDefaultAsync(t => t.Id == cmd.TaskId && t.ChapterId == chapter.Id, ct);
        if (task is null) return Fail($"Ô {cmd.TaskId} không thuộc chương này.", ResultErrorCodes.NotFound);
        var lot = task.LotId is { } lotId ? await _db.StageTaskLots.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lotId, ct) : null;

        if (cmd.Open)
        {
            if (lot?.AssigneeUserId == cmd.UserId ||
                (await TaskGridRules.PendingSubmissionAsync(_db, task, ct))?.SubmittedByUserId == cmd.UserId)
                return Fail("Không được tự duyệt ô mình làm (BR-06).", ResultErrorCodes.Forbidden);
        }
        else if (task.ReviewerUserId != cmd.UserId)
        {
            var workspaceId = await _db.Series.AsNoTracking().Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstAsync(ct);
            var isProducer = await _db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == cmd.UserId
                                                                        && m.Role == WorkspaceRole.Producer, ct);
            if (!isProducer)
                return Fail("Chỉ người đang duyệt hoặc Producer mới nhả được khoá duyệt.", ResultErrorCodes.Forbidden);
        }

        try
        {
            if (cmd.Open) task.OpenReview(cmd.UserId);
            else task.ReleaseReview();
        }
        catch (InvalidOperationException ex) { return Fail(ex.Message, ResultErrorCodes.Conflict); }

        task.UpdatedBy = cmd.UserId.ToString();
        await _db.SaveChangesAsync(ct);
        return Result<StageTaskDto>.Success((await TaskGridRules.WithSummaryAsync(_db, [StageTaskDto.From(task, lot?.AssigneeUserId, isStartable: false)], ct))[0]);
    }
}
