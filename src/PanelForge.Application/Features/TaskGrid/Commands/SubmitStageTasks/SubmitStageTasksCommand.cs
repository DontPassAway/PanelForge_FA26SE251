using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Application.Features.TaskGrid.Models;

namespace PanelForge.Application.Features.TaskGrid.Commands.SubmitStageTasks;

/// <summary>
/// Người nhận nộp một danh sách ô (không khoá theo cả lô), tất cả hoặc không. Công đoạn có cổng duyệt → Submitted,
/// không có → tự duyệt. Chưa ghi bảng Submission (còn gắn với Assignment cũ), sẽ nối ở lát cắt sau.
/// </summary>
public sealed record SubmitStageTasksCommand(Guid ChapterId, IReadOnlyList<Guid> TaskIds, Guid UserId)
    : IRequest<Result<IReadOnlyList<StageTaskDto>>>;

public sealed class SubmitStageTasksCommandHandler : IRequestHandler<SubmitStageTasksCommand, Result<IReadOnlyList<StageTaskDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public SubmitStageTasksCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<StageTaskDto>>> Handle(SubmitStageTasksCommand cmd, CancellationToken ct)
    {
        static Result<IReadOnlyList<StageTaskDto>> Fail(string msg, string code = ResultErrorCodes.Validation) =>
            Result<IReadOnlyList<StageTaskDto>>.Failure(msg, code);

        var ids = cmd.TaskIds.Distinct().ToList();
        if (ids.Count == 0) return Fail("Cần ít nhất một ô để nộp.");

        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null) return Fail($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked) return Fail(locked, ResultErrorCodes.Conflict);

        await _db.LockChapterAsync(chapter.Id, ct);

        var tasks = await _db.StageTasks.Where(t => t.ChapterId == chapter.Id && ids.Contains(t.Id)).ToListAsync(ct);
        if (tasks.Count != ids.Count) return Fail("Có ô không thuộc chương này.", ResultErrorCodes.NotFound);

        var lotIds = tasks.Select(t => t.LotId).OfType<Guid>().Distinct().ToList();
        var assigneeByLot = await _db.StageTaskLots.AsNoTracking().Where(l => lotIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.AssigneeUserId, ct);
        if (tasks.Any(t => t.LotId is not { } lotId || assigneeByLot.GetValueOrDefault(lotId) != cmd.UserId))
            return Fail("Chỉ nộp được ô mình được giao.", ResultErrorCodes.Forbidden);

        var stageIds = tasks.Select(t => t.PipelineStageId).Distinct().ToList();
        var gateByStage = await _db.PipelineStages.AsNoTracking().Where(s => stageIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.IsApprovalGate, ct);

        try
        {
            foreach (var task in tasks)
            {
                task.Submit(submissionId: null, isApprovalGate: gateByStage.GetValueOrDefault(task.PipelineStageId));
                task.UpdatedBy = cmd.UserId.ToString();
            }
        }
        catch (InvalidOperationException ex) { return Fail(ex.Message, ResultErrorCodes.Conflict); }

        await TaskGridRules.RefreshLotsAsync(_db, lotIds.Cast<Guid?>(), tasks, ct);
        await _db.SaveChangesAsync(ct);
        return Result<IReadOnlyList<StageTaskDto>>.Success(
            tasks.Select(t => StageTaskDto.From(t, cmd.UserId, isStartable: false)).ToList());
    }
}
