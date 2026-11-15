using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Application.Features.TaskGrid.Models;

namespace PanelForge.Application.Features.TaskGrid.Commands.ResolveStageTaskImpact;

/// <summary>Producer xử lý Impact Review cho ô mang cờ lỗi thời: Keep giữ bài, Rework đưa ô về ReworkRequired. Cần tiền đề đã duyệt lại.</summary>
public sealed record ResolveStageTaskImpactCommand(Guid ChapterId, Guid TaskId, Guid UserId, bool Keep) : IRequest<Result<StageTaskDto>>, ITransactionalCommand;

public sealed class ResolveStageTaskImpactCommandHandler : IRequestHandler<ResolveStageTaskImpactCommand, Result<StageTaskDto>>
{
    private readonly IPanelForgeDbContext _db;

    public ResolveStageTaskImpactCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<StageTaskDto>> Handle(ResolveStageTaskImpactCommand cmd, CancellationToken ct)
    {
        static Result<StageTaskDto> Fail(string msg, string code) => Result<StageTaskDto>.Failure(msg, code);

        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null) return Fail($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked) return Fail(locked, ResultErrorCodes.Conflict);

        await _db.LockChapterAsync(chapter.Id, ct);

        var chapterTasks = await _db.StageTasks.Where(t => t.ChapterId == chapter.Id).ToListAsync(ct);
        var task = chapterTasks.FirstOrDefault(t => t.Id == cmd.TaskId);
        if (task is null) return Fail($"Ô {cmd.TaskId} không thuộc chương này.", ResultErrorCodes.NotFound);

        var stages = await TaskGridRules.LoadStagesAsync(_db, chapter.SeriesId, ct);
        try { task.ResolveImpact(cmd.Keep, TaskGridRules.PrerequisitesApproved(task, stages, chapterTasks)); }
        catch (InvalidOperationException ex) { return Fail(ex.Message, ResultErrorCodes.Conflict); }

        task.UpdatedBy = cmd.UserId.ToString();
        if (!cmd.Keep && await TaskGridRules.PendingSubmissionAsync(_db, task, ct) is { } pending) pending.Supersede();
        await TaskGridRules.RefreshLotsAsync(_db, [task.LotId], [task], ct);
        await _db.SaveChangesAsync(ct);

        var lot = task.LotId is { } lotId ? await _db.StageTaskLots.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lotId, ct) : null;
        return Result<StageTaskDto>.Success(StageTaskDto.From(task, lot?.AssigneeUserId, isStartable: false));
    }
}
