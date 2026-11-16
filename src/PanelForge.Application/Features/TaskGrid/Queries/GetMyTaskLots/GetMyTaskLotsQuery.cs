using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Application.Features.TaskGrid.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.TaskGrid.Queries.GetMyTaskLots;

/// <summary>
/// Các lô của người gọi trong Workspace, xuyên chương (Clean Cutover: thay my-assignments V2). Luôn bỏ lô Closed;
/// <paramref name="IncludeCompleted"/> = false bỏ thêm lô Completed. Sắp: còn việc trước, hạn gần trước.
/// </summary>
public sealed record GetMyTaskLotsQuery(Guid WorkspaceId, Guid UserId, Guid? SeriesId = null, bool IncludeCompleted = true)
    : IRequest<Result<IReadOnlyList<MyTaskLotDto>>>;

public sealed class GetMyTaskLotsQueryHandler : IRequestHandler<GetMyTaskLotsQuery, Result<IReadOnlyList<MyTaskLotDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public GetMyTaskLotsQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<MyTaskLotDto>>> Handle(GetMyTaskLotsQuery q, CancellationToken ct)
    {
        var series = await _db.Series.AsNoTracking()
            .Where(s => s.WorkspaceId == q.WorkspaceId && (q.SeriesId == null || s.Id == q.SeriesId))
            .Select(s => new { s.Id, s.Title }).ToListAsync(ct);
        var seriesIds = series.Select(s => s.Id).ToList();

        var lots = await _db.StageTaskLots.AsNoTracking()
            .Where(l => l.AssigneeUserId == q.UserId && seriesIds.Contains(l.SeriesId) && l.Status != StageTaskLotStatus.Closed
                && (q.IncludeCompleted || l.Status != StageTaskLotStatus.Completed))
            .ToListAsync(ct);
        if (lots.Count == 0) return Result<IReadOnlyList<MyTaskLotDto>>.Success([]);

        var chapterIds = lots.Select(l => l.ChapterId).Distinct().ToList();
        var chapters = await _db.Chapters.AsNoTracking().Where(c => chapterIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        // Cả ô của người khác: cần để tính tiền đề (IsStartable)
        var tasks = await _db.StageTasks.AsNoTracking().Where(t => chapterIds.Contains(t.ChapterId)).ToListAsync(ct);
        var pageIds = tasks.Select(t => t.PageId).OfType<Guid>().Distinct().ToList();
        var pageNumbers = await _db.Pages.AsNoTracking().Where(p => pageIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.PageNumber, ct);
        var stagesBySeries = new Dictionary<Guid, List<Domain.Entities.Workflow.PipelineStage>>();
        foreach (var seriesId in lots.Select(l => l.SeriesId).Distinct())
            stagesBySeries[seriesId] = await TaskGridRules.LoadStagesAsync(_db, seriesId, ct);

        var lotIds = lots.Select(l => (Guid?)l.Id).ToHashSet();
        var summary = await TaskGridRules.SubmissionSummaryAsync(
            _db, tasks.Where(t => lotIds.Contains(t.LotId)).Select(t => t.Id).ToList(), ct);
        var now = DateTime.UtcNow;
        var result = lots.Select(l =>
        {
            var stages = stagesBySeries[l.SeriesId];
            var stage = stages.First(s => s.Id == l.PipelineStageId);
            var chapter = chapters[l.ChapterId];
            var chapterTasks = tasks.Where(t => t.ChapterId == l.ChapterId).ToList();
            var cells = chapterTasks.Where(t => t.LotId == l.Id && t.Status != StageTaskStatus.Cancelled)
                .Select(t => new MyTaskCellDto(
                    t.Id, t.PageId, t.PageId is { } pid && pageNumbers.TryGetValue(pid, out var no) ? no : null, t.Status,
                    t.Status is StageTaskStatus.Assigned or StageTaskStatus.ReworkRequired && !t.IsInputOutdated
                        && TaskGridRules.PrerequisitesApproved(t, stages, chapterTasks),
                    t.IsInputOutdated, t.OutdatedReason,
                    summary.TryGetValue(t.Id, out var s) ? s.Comment : null,
                    summary.TryGetValue(t.Id, out var a) ? a.Attempts : 0, t.ReopenReason))
                .OrderBy(c => c.PageNumber ?? 0).ToList();
            return new MyTaskLotDto(
                l.Id, l.SeriesId, series.First(s => s.Id == l.SeriesId).Title, l.ChapterId, chapter.ChapterNumber, chapter.Title,
                stage.Id, stage.Name, stage.StageOrder, stage.ColorCode, l.AssignedRole, l.Brief, l.DueDate, l.Status,
                l.IsOverdue(now), cells);
        })
            .OrderBy(l => l.Status != StageTaskLotStatus.Active)
            .ThenBy(l => l.DueDate == null).ThenBy(l => l.DueDate)
            .ThenBy(l => l.ChapterNumber).ThenBy(l => l.StageOrder)
            .ToList();
        return Result<IReadOnlyList<MyTaskLotDto>>.Success(result);
    }
}
