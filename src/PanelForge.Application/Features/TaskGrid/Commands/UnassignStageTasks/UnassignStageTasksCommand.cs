using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Application.Features.TaskGrid.Models;

namespace PanelForge.Application.Features.TaskGrid.Commands.UnassignStageTasks;

/// <summary>
/// Producer gỡ người khỏi danh sách ô (Assigned/InProgress/ReworkRequired), tất cả hoặc không. Ô về Unassigned để giao lại
/// qua POST /lots (Transfer = bỏ gán + giao lô mới). Lô không còn ô nào thì Closed.
/// </summary>
public sealed record UnassignStageTasksCommand(Guid ChapterId, IReadOnlyList<Guid> TaskIds, Guid UserId)
    : IRequest<Result<IReadOnlyList<StageTaskDto>>>, ITransactionalCommand;

public sealed class UnassignStageTasksCommandHandler : IRequestHandler<UnassignStageTasksCommand, Result<IReadOnlyList<StageTaskDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public UnassignStageTasksCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<StageTaskDto>>> Handle(UnassignStageTasksCommand cmd, CancellationToken ct)
    {
        static Result<IReadOnlyList<StageTaskDto>> Fail(string msg, string code = ResultErrorCodes.Validation) =>
            Result<IReadOnlyList<StageTaskDto>>.Failure(msg, code);

        var ids = cmd.TaskIds.Distinct().ToList();
        if (ids.Count == 0) return Fail("Cần ít nhất một ô để bỏ gán.");

        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null) return Fail($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked) return Fail(locked, ResultErrorCodes.Conflict);

        await _db.LockChapterAsync(chapter.Id, ct);

        var tasks = await _db.StageTasks.Where(t => t.ChapterId == chapter.Id && ids.Contains(t.Id)).ToListAsync(ct);
        if (tasks.Count != ids.Count) return Fail("Có ô không thuộc chương này.", ResultErrorCodes.NotFound);

        var oldLotIds = tasks.Select(t => t.LotId).ToList();
        try
        {
            foreach (var task in tasks)
            {
                task.Unassign();
                task.UpdatedBy = cmd.UserId.ToString();
            }
        }
        catch (InvalidOperationException ex) { return Fail(ex.Message, ResultErrorCodes.Conflict); }

        var assigneeByLot = await _db.StageTaskLots.AsNoTracking().Where(l => oldLotIds.Contains((Guid?)l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.AssigneeUserId, ct);
        foreach (var group in oldLotIds.OfType<Guid>().GroupBy(id => assigneeByLot.GetValueOrDefault(id)).Where(g => g.Key != Guid.Empty))
            _db.Notifications.Add(TaskGridNotifications.Unassigned(group.Key, chapter.Id, group.Count()));

        await TaskGridRules.RefreshLotsAsync(_db, oldLotIds, tasks, ct);
        await _db.SaveChangesAsync(ct);
        return Result<IReadOnlyList<StageTaskDto>>.Success(
            tasks.Select(t => StageTaskDto.From(t, assigneeUserId: null, isStartable: false)).ToList());
    }
}
