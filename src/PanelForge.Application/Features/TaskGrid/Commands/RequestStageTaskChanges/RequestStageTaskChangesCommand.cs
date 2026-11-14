using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Application.Features.TaskGrid.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.TaskGrid.Commands.RequestStageTaskChanges;

/// <summary>Editor/Producer trả ô đã nộp về ReworkRequired (BR-06: khác người làm; ô đang do người khác duyệt thì không chồng).</summary>
public sealed record RequestStageTaskChangesCommand(Guid ChapterId, Guid TaskId, Guid UserId) : IRequest<Result<StageTaskDto>>;

public sealed class RequestStageTaskChangesCommandHandler : IRequestHandler<RequestStageTaskChangesCommand, Result<StageTaskDto>>
{
    private readonly IPanelForgeDbContext _db;

    public RequestStageTaskChangesCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<StageTaskDto>> Handle(RequestStageTaskChangesCommand cmd, CancellationToken ct)
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
        if (lot?.AssigneeUserId == cmd.UserId)
            return Fail("Không được tự duyệt ô mình làm (BR-06).", ResultErrorCodes.Forbidden);
        if (task.Status == StageTaskStatus.UnderReview && task.ReviewerUserId is { } reviewer && reviewer != cmd.UserId)
            return Fail("Ô này đang do người khác duyệt.", ResultErrorCodes.Conflict);

        var stages = await TaskGridRules.LoadStagesAsync(_db, chapter.SeriesId, ct);
        try
        {
            if (task.Status == StageTaskStatus.Submitted) task.OpenReview(cmd.UserId);
            task.RequestChanges(TaskGridRules.PrerequisitesApproved(task, stages, chapterTasks));
        }
        catch (InvalidOperationException ex) { return Fail(ex.Message, ResultErrorCodes.Conflict); }

        task.UpdatedBy = cmd.UserId.ToString();
        await _db.SaveChangesAsync(ct);
        return Result<StageTaskDto>.Success(StageTaskDto.From(task, lot?.AssigneeUserId, isStartable: false));
    }
}
