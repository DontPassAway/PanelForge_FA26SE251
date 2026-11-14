using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Application.Features.TaskGrid.Models;

namespace PanelForge.Application.Features.TaskGrid.Commands.StartStageTask;

/// <summary>Người nhận lô bắt đầu một ô. BR-19: chỉ khi mọi ô tiền đề (theo nhánh) đã duyệt và ô không mang cờ lỗi thời.</summary>
public sealed record StartStageTaskCommand(Guid ChapterId, Guid TaskId, Guid UserId) : IRequest<Result<StageTaskDto>>;

public sealed class StartStageTaskCommandHandler : IRequestHandler<StartStageTaskCommand, Result<StageTaskDto>>
{
    private readonly IPanelForgeDbContext _db;

    public StartStageTaskCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<StageTaskDto>> Handle(StartStageTaskCommand cmd, CancellationToken ct)
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
        if (lot is null || lot.AssigneeUserId != cmd.UserId)
            return Fail("Chỉ người được giao ô này mới bắt đầu được.", ResultErrorCodes.Forbidden);

        var stages = await TaskGridRules.LoadStagesAsync(_db, chapter.SeriesId, ct);
        try { task.Start(TaskGridRules.PrerequisitesApproved(task, stages, chapterTasks)); }
        catch (InvalidOperationException ex) { return Fail(ex.Message, ResultErrorCodes.Conflict); }

        task.UpdatedBy = cmd.UserId.ToString();
        await _db.SaveChangesAsync(ct);
        return Result<StageTaskDto>.Success(StageTaskDto.From(task, lot.AssigneeUserId, isStartable: false));
    }
}
