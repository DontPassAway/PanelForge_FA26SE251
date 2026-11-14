using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Application.Features.TaskGrid.Models;

namespace PanelForge.Application.Features.TaskGrid.Commands.ReopenStageTask;

/// <summary>
/// Producer mở lại ô đã duyệt (thay ReturnWork): ô về ReworkRequired, mọi ô hạ nguồn đã bắt đầu bị gắn cờ đầu vào lỗi thời
/// và phải qua Impact Review. Ô hạ nguồn chưa bắt đầu tự bị chặn Start vì tiền đề không còn Approved.
/// </summary>
public sealed record ReopenStageTaskCommand(Guid ChapterId, Guid TaskId, Guid UserId, string Reason)
    : IRequest<Result<ReopenStageTaskResultDto>>;

public sealed class ReopenStageTaskCommandHandler : IRequestHandler<ReopenStageTaskCommand, Result<ReopenStageTaskResultDto>>
{
    private readonly IPanelForgeDbContext _db;

    public ReopenStageTaskCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ReopenStageTaskResultDto>> Handle(ReopenStageTaskCommand cmd, CancellationToken ct)
    {
        static Result<ReopenStageTaskResultDto> Fail(string msg, string code = ResultErrorCodes.Validation) =>
            Result<ReopenStageTaskResultDto>.Failure(msg, code);

        if (string.IsNullOrWhiteSpace(cmd.Reason)) return Fail("Cần nêu lý do mở lại.");

        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null) return Fail($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked) return Fail(locked, ResultErrorCodes.Conflict);

        await _db.LockChapterAsync(chapter.Id, ct);

        var chapterTasks = await _db.StageTasks.Where(t => t.ChapterId == chapter.Id).ToListAsync(ct);
        var task = chapterTasks.FirstOrDefault(t => t.Id == cmd.TaskId);
        if (task is null) return Fail($"Ô {cmd.TaskId} không thuộc chương này.", ResultErrorCodes.NotFound);

        try { task.Reopen(); }
        catch (InvalidOperationException ex) { return Fail(ex.Message, ResultErrorCodes.Conflict); }

        var stages = await TaskGridRules.LoadStagesAsync(_db, chapter.SeriesId, ct);
        var stageName = stages.FirstOrDefault(s => s.Id == task.PipelineStageId)?.Name ?? "Công đoạn trước";
        var outdated = new List<Guid>();
        foreach (var downstream in TaskGridRules.Downstream(task, stages, chapterTasks))
        {
            var wasOutdated = downstream.IsInputOutdated;
            downstream.MarkInputOutdated($"{stageName} được mở lại: {cmd.Reason.Trim()}");
            if (!downstream.IsInputOutdated || wasOutdated) continue;
            downstream.UpdatedBy = cmd.UserId.ToString();
            outdated.Add(downstream.Id);
        }

        task.UpdatedBy = cmd.UserId.ToString();
        await TaskGridRules.RefreshLotsAsync(_db, [task.LotId], [task], ct);
        await _db.SaveChangesAsync(ct);

        var lot = task.LotId is { } lotId ? await _db.StageTaskLots.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lotId, ct) : null;
        return Result<ReopenStageTaskResultDto>.Success(new(StageTaskDto.From(task, lot?.AssigneeUserId, isStartable: false), outdated));
    }
}
