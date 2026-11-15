using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Application.Features.TaskGrid.Models;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.TaskGrid.Commands.AssignStageTaskLots;

/// <summary>
/// Producer giao nhiều lô trong một lần, tất cả hoặc không: mỗi lô = một người + các ô Unassigned cùng công đoạn.
/// Người nhận phải là thành viên Active của Workspace, đúng vai trò của công đoạn. <c>DryRun</c> chỉ kiểm tra.
/// </summary>
public sealed record AssignStageTaskLotsCommand(
    Guid ChapterId, Guid UserId, IReadOnlyList<StageTaskLotRequest> Lots, bool DryRun, bool Reassign = false) : IRequest<Result<AssignStageTaskLotsResultDto>>, ITransactionalCommand;

public sealed class AssignStageTaskLotsCommandHandler : IRequestHandler<AssignStageTaskLotsCommand, Result<AssignStageTaskLotsResultDto>>
{
    private readonly IPanelForgeDbContext _db;

    public AssignStageTaskLotsCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<AssignStageTaskLotsResultDto>> Handle(AssignStageTaskLotsCommand cmd, CancellationToken ct)
    {
        static Result<AssignStageTaskLotsResultDto> Fail(string msg, string code = ResultErrorCodes.Validation) =>
            Result<AssignStageTaskLotsResultDto>.Failure(msg, code);

        if (cmd.Lots.Count == 0) return Fail("Cần ít nhất một lô.");
        if (cmd.Lots.Any(l => l.TaskIds.Count == 0)) return Fail("Không tạo lô rỗng: mỗi lô cần ít nhất một ô.");
        var allTaskIds = cmd.Lots.SelectMany(l => l.TaskIds).ToList();
        if (allTaskIds.Distinct().Count() != allTaskIds.Count) return Fail("Một ô không được nằm trong hai lô.");

        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null) return Fail($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked) return Fail(locked, ResultErrorCodes.Conflict);

        if (!cmd.DryRun) await _db.LockChapterAsync(chapter.Id, ct);

        var workspaceId = await _db.Series.AsNoTracking()
            .Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstOrDefaultAsync(ct);
        var tasks = await _db.StageTasks.Where(t => t.ChapterId == chapter.Id && allTaskIds.Contains(t.Id)).ToListAsync(ct);
        if (tasks.Count != allTaskIds.Count) return Fail("Có ô không thuộc chương này.", ResultErrorCodes.NotFound);

        var stageIds = cmd.Lots.Select(l => l.PipelineStageId).Distinct().ToList();
        var stages = await _db.PipelineStages.AsNoTracking().Where(s => stageIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var assigneeIds = cmd.Lots.Select(l => l.AssigneeUserId).Distinct().ToList();
        var members = await _db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && assigneeIds.Contains(m.UserId) && m.Status == WorkspaceMemberStatus.Active)
            .ToDictionaryAsync(m => m.UserId, m => m.Role, ct);

        var lots = new List<(StageTaskLot Lot, List<StageTask> Tasks)>();
        foreach (var request in cmd.Lots)
        {
            if (!stages.TryGetValue(request.PipelineStageId, out var stage) || !stage.IsEnabled)
                return Fail($"Công đoạn {request.PipelineStageId} không tồn tại hoặc đang tắt.", ResultErrorCodes.NotFound);
            if (!members.TryGetValue(request.AssigneeUserId, out var role))
                return Fail("Người nhận không phải thành viên đang hoạt động của Workspace.", ResultErrorCodes.Conflict);
            if (stage.AllowedRole is { } allowed && role != allowed)
                return Fail($"Công đoạn '{stage.Name}' cần vai trò {allowed}, người nhận đang là {role}.", ResultErrorCodes.Conflict);

            var lotTasks = tasks.Where(t => request.TaskIds.Contains(t.Id)).ToList();
            if (lotTasks.Any(t => t.PipelineStageId != stage.Id))
                return Fail($"Lô của công đoạn '{stage.Name}' chứa ô của công đoạn khác.");
            // Reassign ("người cứu viện"): chuyển ô chưa nộp sang người khác trong 1 bước; ô đã nộp/duyệt không đổi người được,
            // ô lỗi thời phải qua Impact Review trước (AssignTo xoá cờ lỗi thời).
            if (lotTasks.FirstOrDefault(t => cmd.Reassign
                    ? t.Status is not (StageTaskStatus.Unassigned or StageTaskStatus.Assigned or StageTaskStatus.InProgress
                        or StageTaskStatus.ReworkRequired) || t.IsInputOutdated
                    : t.Status != StageTaskStatus.Unassigned) is { } taken)
                return Fail(cmd.Reassign
                    ? $"Ô {taken.Id} đang {taken.Status}{(taken.IsInputOutdated ? " (lỗi thời)" : "")}, không đổi người được."
                    : $"Ô {taken.Id} đang ở trạng thái {taken.Status}, chỉ giao được ô chưa giao.", ResultErrorCodes.Conflict);

            StageTaskLot lot;
            try
            {
                lot = StageTaskLot.Create(chapter.SeriesId, chapter.Id, stage.Id, request.AssigneeUserId, role,
                    cmd.UserId, request.Brief, request.DueDate);
            }
            catch (ArgumentException ex) { return Fail(ex.Message); }
            lots.Add((lot, lotTasks));
        }

        var now = DateTime.UtcNow;
        if (cmd.DryRun)
            return Result<AssignStageTaskLotsResultDto>.Success(new AssignStageTaskLotsResultDto(true, tasks.Count, []));

        var oldLotIds = tasks.Select(t => t.LotId).Where(id => id != null).Distinct().ToList();
        if (oldLotIds.Count > 0)
            foreach (var old in await _db.StageTaskLots.AsNoTracking().Where(l => oldLotIds.Contains(l.Id)).ToListAsync(ct))
                _db.Notifications.Add(TaskGridNotifications.Unassigned(old.AssigneeUserId, chapter.Id,
                    tasks.Count(t => t.LotId == old.Id)));

        foreach (var (lot, lotTasks) in lots)
        {
            lot.CreatedBy = cmd.UserId.ToString();
            _db.StageTaskLots.Add(lot);
            foreach (var task in lotTasks) task.AssignTo(lot.Id);
            _db.Notifications.Add(TaskGridNotifications.Assigned(lot, lotTasks.Count, stages[lot.PipelineStageId].Name));
        }
        // Người nhận phải có trong SeriesMembers, nếu không vào trang/chương sẽ bị RequireWorkspaceRole chặn 403 (như V2).
        foreach (var (lot, _) in lots.DistinctBy(x => (x.Lot.AssigneeUserId, x.Lot.AssignedRole)))
            await AssignmentRules.EnsureSeriesMemberAsync(_db, chapter.SeriesId, lot.AssigneeUserId, lot.AssignedRole, ct);
        await TaskGridRules.RefreshLotsAsync(_db, oldLotIds, tasks, ct);

        await _db.SaveChangesAsync(ct);
        return Result<AssignStageTaskLotsResultDto>.Success(new AssignStageTaskLotsResultDto(
            false, tasks.Count, lots.Select(x => StageTaskLotDto.From(x.Lot, x.Tasks, now)).ToList()));
    }
}
