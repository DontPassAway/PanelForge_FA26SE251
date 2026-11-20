using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Application.Features.TaskGrid.Models;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.TaskGrid.Queries.GetAutoAssignPlan;

/// <summary>
/// Lập phương án giao các ô chưa giao của chương; không ghi gì. Người dùng xác nhận bằng POST /lots.
/// Khối lượng một ô = số panel của trang (tối thiểu 1). Trang đã có người làm ở công đoạn trước gần nhất thì giữ đúng người đó;
/// các ô còn lại chia dải trang liền nhau sao cho tổng khối lượng việc chưa xong trong Workspace cân nhau.
/// Hoà thì ưu tiên người được giao lâu nhất trước, để phần dư không luôn rơi vào cùng một người.
/// </summary>
public sealed record GetAutoAssignPlanQuery(Guid ChapterId) : IRequest<Result<AutoAssignPlanDto>>;

public sealed class GetAutoAssignPlanQueryHandler : IRequestHandler<GetAutoAssignPlanQuery, Result<AutoAssignPlanDto>>
{
    private readonly IPanelForgeDbContext _db;

    public GetAutoAssignPlanQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<AutoAssignPlanDto>> Handle(GetAutoAssignPlanQuery q, CancellationToken ct)
    {
        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == q.ChapterId, ct);
        if (chapter is null)
            return Result<AutoAssignPlanDto>.Failure($"Chapter {q.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked)
            return Result<AutoAssignPlanDto>.Failure(locked, ResultErrorCodes.Conflict);

        var workspaceId = await _db.Series.AsNoTracking()
            .Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstAsync(ct);
        var seriesIds = await _db.Series.AsNoTracking()
            .Where(s => s.WorkspaceId == workspaceId).Select(s => s.Id).ToListAsync(ct);
        var members = await _db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && m.Status == WorkspaceMemberStatus.Active)
            .Select(m => new { m.UserId, m.Role }).ToListAsync(ct);
        var memberIds = members.Select(m => m.UserId).ToList();
        var names = await _db.Users.AsNoTracking().Where(u => memberIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var openCells = await (
                from t in _db.StageTasks.AsNoTracking()
                join l in _db.StageTaskLots.AsNoTracking() on t.LotId equals (Guid?)l.Id
                where memberIds.Contains(l.AssigneeUserId) && seriesIds.Contains(t.SeriesId)
                      && t.Status != StageTaskStatus.Approved && t.Status != StageTaskStatus.Cancelled
                select new { l.AssigneeUserId, t.PageId }).ToListAsync(ct);
        var lastAssigned = (await _db.StageTaskLots.AsNoTracking()
                .Where(l => memberIds.Contains(l.AssigneeUserId))
                .Select(l => new { l.AssigneeUserId, l.CreatedAt }).ToListAsync(ct))
            .GroupBy(l => l.AssigneeUserId).ToDictionary(g => g.Key, g => g.Max(l => l.CreatedAt));

        var pageNumbers = await _db.Pages.AsNoTracking().Where(p => p.ChapterId == chapter.Id)
            .ToDictionaryAsync(p => p.Id, p => p.PageNumber, ct);
        var weightedPageIds = openCells.Where(c => c.PageId != null).Select(c => c.PageId!.Value)
            .Concat(pageNumbers.Keys).Distinct().ToList();
        var panelCounts = (await _db.Panels.AsNoTracking().Where(p => weightedPageIds.Contains(p.PageId))
                .Select(p => p.PageId).ToListAsync(ct))
            .GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
        int Weight(Guid? pageId) => pageId is { } id ? Math.Max(1, panelCounts.GetValueOrDefault(id)) : 1;

        var openCount = memberIds.ToDictionary(id => id, id => openCells.Count(c => c.AssigneeUserId == id));
        var openUnits = memberIds.ToDictionary(id => id, id => openCells.Where(c => c.AssigneeUserId == id).Sum(c => Weight(c.PageId)));
        var load = new Dictionary<Guid, int>(openUnits);

        var stages = await TaskGridRules.LoadStagesAsync(_db, chapter.SeriesId, ct);
        var stageOrder = stages.ToDictionary(s => s.Id, s => s.StageOrder);
        var tasks = await _db.StageTasks.AsNoTracking().Where(t => t.ChapterId == chapter.Id).ToListAsync(ct);
        var assigneeByLot = await _db.StageTaskLots.AsNoTracking().Where(l => l.ChapterId == chapter.Id)
            .ToDictionaryAsync(l => l.Id, l => l.AssigneeUserId, ct);

        // Người đang giữ từng trang theo công đoạn (cả ô đã giao và ô vừa lập phương án)
        var owners = new List<(Guid PageId, int Order, Guid UserId)>();
        foreach (var t in tasks)
            if (t.LotId is { } lotId && t.PageId is { } pageId && t.Status != StageTaskStatus.Cancelled
                && assigneeByLot.TryGetValue(lotId, out var owner) && stageOrder.TryGetValue(t.PipelineStageId, out var order))
                owners.Add((pageId, order, owner));

        var generated = stages.Where(s => s.IsEnabled && !(s.IsTerminal && !s.IsApprovalGate)).ToList();
        var lots = new List<AutoAssignLotDto>();
        var skipped = new List<AutoAssignSkippedDto>();
        var unassigned = tasks.Where(t => t.Status == StageTaskStatus.Unassigned).ToList();

        foreach (var stage in stages.Where(s => s.IsEnabled))
        {
            var cells = unassigned.Where(t => t.PipelineStageId == stage.Id)
                .OrderBy(t => t.PageId is { } id ? pageNumbers.GetValueOrDefault(id) : 0).ToList();
            if (cells.Count == 0) continue;

            if (stage.AllowedRole is not { } role)
            {
                skipped.Add(new(stage.Id, stage.Name, cells.Count, "Công đoạn chưa cấu hình vai trò phụ trách."));
                continue;
            }
            var candidates = members.Where(m => m.Role == role).Select(m => m.UserId)
                .OrderBy(id => lastAssigned.TryGetValue(id, out var at) ? at : DateTime.MinValue).ThenBy(id => id).ToList();
            if (candidates.Count == 0)
            {
                skipped.Add(new(stage.Id, stage.Name, cells.Count, $"Không có thành viên Active vai trò {role}."));
                continue;
            }

            var picked = candidates.ToDictionary(id => id, _ => new List<StageTask>());
            var rest = new List<StageTask>();
            foreach (var cell in cells)
            {
                // Giữ người đã làm đúng trang này ở công đoạn trước gần nhất
                var previous = cell.PageId is { } pageId
                    ? owners.Where(o => o.PageId == pageId && o.Order < stage.StageOrder && picked.ContainsKey(o.UserId))
                        .OrderByDescending(o => o.Order).Select(o => (Guid?)o.UserId).FirstOrDefault()
                    : null;
                if (previous is { } keep) picked[keep].Add(cell);
                else rest.Add(cell);
            }
            foreach (var (userId, list) in picked) load[userId] += list.Sum(c => Weight(c.PageId));

            if (rest.Count > 0)
            {
                // Chỉ tiêu khối lượng: từng ô cho người đang nhẹ nhất (hoà thì người được giao lâu nhất), rồi cắt dải liền nhau
                var target = candidates.ToDictionary(id => id, _ => 0);
                foreach (var cell in rest)
                    target[candidates.MinBy(id => load[id] + target[id])] += Weight(cell.PageId);

                var queue = new Queue<Guid>(candidates.Where(id => target[id] > 0));
                var current = queue.Dequeue();
                var given = 0;
                foreach (var cell in rest)
                {
                    var w = Weight(cell.PageId);
                    if (given > 0 && given + w / 2.0 > target[current] && queue.Count > 0)
                    {
                        current = queue.Dequeue();
                        given = 0;
                    }
                    picked[current].Add(cell);
                    given += w;
                    load[current] += w;
                }
            }

            var due = SuggestedDueDate(chapter.TargetReleaseDate, stage, generated);
            foreach (var userId in candidates.Where(id => picked[id].Count > 0))
            {
                var part = picked[userId].OrderBy(t => t.PageId is { } id ? pageNumbers.GetValueOrDefault(id) : 0).ToList();
                var pages = part.Where(t => t.PageId is not null && pageNumbers.ContainsKey(t.PageId.Value))
                    .Select(t => pageNumbers[t.PageId!.Value]).ToList();
                lots.Add(new(stage.Id, stage.Name, userId, names.GetValueOrDefault(userId, ""), role,
                    part.Select(t => t.Id).ToList(), pages, openCount.GetValueOrDefault(userId),
                    part.Sum(t => Weight(t.PageId)), openUnits.GetValueOrDefault(userId), due));
                foreach (var t in part.Where(t => t.PageId is not null)) owners.Add((t.PageId!.Value, stage.StageOrder, userId));
            }
        }

        return Result<AutoAssignPlanDto>.Success(new(chapter.Id, unassigned.Count, lots, skipped));
    }

    /// <summary>
    /// Hạn gợi ý = ngày phát hành dự kiến trừ thời lượng ước tính của các công đoạn phía sau (nhánh song song cũng cộng dồn, nên hạn hơi sớm).
    /// Không có ngày phát hành thì không gợi ý.
    /// </summary>
    internal static DateTime? SuggestedDueDate(DateOnly? releaseDate, PipelineStage stage, IReadOnlyList<PipelineStage> generated)
    {
        if (releaseDate is not { } release) return null;
        var laterDays = generated.Where(s => s.StageOrder > stage.StageOrder).Sum(s => s.EstimatedDurationDays ?? 0);
        return DateTime.SpecifyKind(release.AddDays(-laterDays).ToDateTime(new TimeOnly(23, 59)), DateTimeKind.Utc);
    }
}
