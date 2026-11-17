using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Application.Features.TaskGrid.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.TaskGrid.Commands.ApproveStageTask;

/// <summary>
/// Editor/Producer duyệt một ô đã nộp (tự mở duyệt nếu ô còn Submitted). BR-06: người duyệt khác người làm ô;
/// ô đang do người khác mở duyệt thì không duyệt chồng.
/// </summary>
public sealed record ApproveStageTaskCommand(Guid ChapterId, Guid TaskId, Guid UserId) : IRequest<Result<StageTaskDto>>, ITransactionalCommand;

public sealed class ApproveStageTaskCommandHandler : IRequestHandler<ApproveStageTaskCommand, Result<StageTaskDto>>
{
    private readonly IPanelForgeDbContext _db;

    public ApproveStageTaskCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<StageTaskDto>> Handle(ApproveStageTaskCommand cmd, CancellationToken ct)
    {
        static Result<StageTaskDto> Fail(string msg, string code) => Result<StageTaskDto>.Failure(msg, code);

        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null) return Fail($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked) return Fail(locked, ResultErrorCodes.Conflict);

        await _db.LockChapterAsync(chapter.Id, ct);

        var chapterTasks = await _db.StageTasks.Where(t => t.ChapterId == chapter.Id).ToListAsync(ct);
        var task = chapterTasks.FirstOrDefault(t => t.Id == cmd.TaskId);
        if (task is null) return Fail($"Ô {cmd.TaskId} không thuộc chương này.", ResultErrorCodes.NotFound);

        var lot = task.LotId is { } lotId ? await _db.StageTaskLots.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lotId, ct) : null;
        // BR-06: người duyệt khác cả người đang giữ lô lẫn người đã nộp bản đang chờ (ô có thể đã đổi người sau khi nộp)
        if (lot?.AssigneeUserId == cmd.UserId ||
            (await TaskGridRules.PendingSubmissionAsync(_db, task, ct))?.SubmittedByUserId == cmd.UserId)
            return Fail("Không được tự duyệt ô mình làm (BR-06).", ResultErrorCodes.Forbidden);
        if (task.Status == StageTaskStatus.UnderReview && task.ReviewerUserId is { } reviewer && reviewer != cmd.UserId)
            return Fail("Ô này đang do người khác duyệt.", ResultErrorCodes.Conflict);

        try
        {
            if (task.Status == StageTaskStatus.Submitted) task.OpenReview(cmd.UserId);
            task.Approve();
        }
        catch (InvalidOperationException ex) { return Fail(ex.Message, ResultErrorCodes.Conflict); }

        task.UpdatedBy = cmd.UserId.ToString();
        if (await TaskGridRules.PendingSubmissionAsync(_db, task, ct) is { } submission) submission.Approve(cmd.UserId);

        var stages = await TaskGridRules.LoadStagesAsync(_db, chapter.SeriesId, ct);
        var stageName = stages.FirstOrDefault(s => s.Id == task.PipelineStageId)?.Name ?? "?";
        if (lot is not null) _db.Notifications.Add(TaskGridNotifications.Approved(lot.AssigneeUserId, chapter.Id, stageName));
        await TaskGridNotifications.AddReadyAsync(_db, [task], stages, chapterTasks, chapter.Id, ct);
        await TaskGridRules.RefreshLotsAsync(_db, [task.LotId], [task], ct);
        await _db.SaveChangesAsync(ct);
        return Result<StageTaskDto>.Success((await TaskGridRules.WithSummaryAsync(_db, [StageTaskDto.From(task, lot?.AssigneeUserId, isStartable: false)], ct))[0]);
    }
}
