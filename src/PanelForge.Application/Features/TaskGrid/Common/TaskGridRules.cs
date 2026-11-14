using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.TaskGrid.Common;

internal static class TaskGridRules
{
    /// <summary>Công đoạn (đang bật và đã tắt) của pipeline Series, theo thứ tự.</summary>
    public static async Task<List<PipelineStage>> LoadStagesAsync(IPanelForgeDbContext db, Guid seriesId, CancellationToken ct)
    {
        var pipelineIds = await db.PipelineDefinitions.AsNoTracking()
            .Where(d => d.SeriesId == seriesId).Select(d => d.Id).ToListAsync(ct);
        return await db.PipelineStages.AsNoTracking()
            .Where(s => pipelineIds.Contains(s.PipelineDefinitionId))
            .OrderBy(s => s.StageOrder)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Mọi ô tiền đề trực tiếp (BR-19, theo nhánh) đã Approved và không mang cờ lỗi thời.
    /// Ô cấp Trang chờ ô cùng trang (hoặc ô cấp Chương) của công đoạn trước; ô cấp Chương chờ mọi ô chưa huỷ của công đoạn trước.
    /// Công đoạn trước chưa sinh ô nào thì coi như chưa xong.
    /// </summary>
    public static bool PrerequisitesApproved(StageTask task, IReadOnlyList<PipelineStage> stages, IReadOnlyList<StageTask> chapterTasks)
    {
        var stage = stages.FirstOrDefault(s => s.Id == task.PipelineStageId);
        if (stage is null) return false;

        foreach (var prerequisite in StageDependencies.DirectPrerequisites(stage, stages))
        {
            var cells = chapterTasks.Where(t => t.PipelineStageId == prerequisite.Id && t.Status != StageTaskStatus.Cancelled);
            var needed = (task.PageId is { } pageId ? cells.Where(t => t.PageId == pageId || t.PageId == null) : cells).ToList();
            if (needed.Count == 0) return false;
            if (needed.Any(t => t.Status != StageTaskStatus.Approved || t.IsInputOutdated)) return false;
        }
        return true;
    }

    /// <summary>
    /// Ô hạ nguồn (bắc cầu theo BR-19) của <paramref name="task"/>, chưa huỷ: cùng trang, hoặc một bên là ô cấp Chương.
    /// Dùng khi mở lại ô để gắn cờ đầu vào lỗi thời.
    /// </summary>
    public static List<StageTask> Downstream(StageTask task, IReadOnlyList<PipelineStage> stages, IReadOnlyList<StageTask> chapterTasks)
    {
        var reached = new HashSet<Guid> { task.PipelineStageId };
        var queue = new Queue<Guid>(reached);
        while (queue.TryDequeue(out var current))
            foreach (var s in stages.Where(s => s.IsEnabled && !reached.Contains(s.Id)
                         && StageDependencies.DirectPrerequisites(s, stages).Any(p => p.Id == current)).ToList())
            {
                reached.Add(s.Id);
                queue.Enqueue(s.Id);
            }
        reached.Remove(task.PipelineStageId);

        return chapterTasks.Where(t => t.Id != task.Id && t.Status != StageTaskStatus.Cancelled
                && reached.Contains(t.PipelineStageId)
                && (task.PageId is null || t.PageId is null || t.PageId == task.PageId))
            .ToList();
    }

    /// <summary>Lần nộp đang chờ quyết định của ô (theo CurrentSubmissionId), hoặc null.</summary>
    public static Task<StageTaskSubmission?> PendingSubmissionAsync(IPanelForgeDbContext db, StageTask task, CancellationToken ct) =>
        task.CurrentSubmissionId is { } id
            ? db.StageTaskSubmissions.FirstOrDefaultAsync(s => s.Id == id && s.Status == SubmissionStatus.Pending, ct)
            : Task.FromResult<StageTaskSubmission?>(null);

    public static async Task RefreshLotsAsync(IPanelForgeDbContext db, IEnumerable<Guid?> lotIds, IReadOnlyList<StageTask> changed, CancellationToken ct)
    {
        var ids = lotIds.OfType<Guid>().Distinct().ToList();
        if (ids.Count == 0) return;

        var lots = await db.StageTaskLots.Where(l => ids.Contains(l.Id)).ToListAsync(ct);
        var stored = await db.StageTasks.AsNoTracking()
            .Where(t => t.LotId != null && ids.Contains(t.LotId.Value)).ToListAsync(ct);
        var changedById = changed.ToDictionary(t => t.Id);

        foreach (var lot in lots)
        {
            // Trạng thái mới nhất: bản vừa đổi trong bộ nhớ thắng bản đọc từ DB
            var statuses = stored.Where(t => t.LotId == lot.Id && !changedById.ContainsKey(t.Id))
                .Concat(changed.Where(t => t.LotId == lot.Id))
                .Select(t => t.Status)
                .ToList();
            lot.RefreshStatus(statuses);
        }
    }
}
