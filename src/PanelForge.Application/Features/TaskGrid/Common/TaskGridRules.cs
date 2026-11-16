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

    /// <summary>Theo ô: nhận xét của lần bị yêu cầu sửa gần nhất và số lần đã nộp (để người làm thấy ngay cần sửa gì).</summary>
    public static async Task<Dictionary<Guid, (string? Comment, int Attempts)>> SubmissionSummaryAsync(
        IPanelForgeDbContext db, IReadOnlyCollection<Guid> taskIds, CancellationToken ct)
    {
        var rows = await db.StageTaskSubmissions.AsNoTracking()
            .Where(s => taskIds.Contains(s.StageTaskId))
            .Select(s => new { s.StageTaskId, s.AttemptNumber, s.Status, s.DecisionComment })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.StageTaskId).ToDictionary(g => g.Key, g => (
            g.Where(r => r.Status == SubmissionStatus.Rejected).OrderByDescending(r => r.AttemptNumber).FirstOrDefault()?.DecisionComment,
            g.Max(r => r.AttemptNumber)));
    }

    /// <summary>Gắn nhận xét lần sửa gần nhất + số lần nộp vào DTO trả về của các lệnh ghi (để FE khỏi phải tải lại lưới).</summary>
    public static async Task<IReadOnlyList<Models.StageTaskDto>> WithSummaryAsync(
        IPanelForgeDbContext db, IEnumerable<Models.StageTaskDto> dtos, CancellationToken ct)
    {
        var list = dtos.ToList();
        var summary = await SubmissionSummaryAsync(db, list.Select(d => d.Id).ToList(), ct);
        return list.Select(d => summary.TryGetValue(d.Id, out var s)
            ? d with { LastDecisionComment = s.Comment, AttemptCount = s.Attempts } : d).ToList();
    }

    /// <summary>Ô chưa xong (chưa duyệt, chưa huỷ) thuộc lô của người dùng; <paramref name="seriesIds"/> null = mọi Series.</summary>
    public static IQueryable<StageTask> UnfinishedCellsOf(
        IPanelForgeDbContext db, Guid userId, IReadOnlyCollection<Guid>? seriesIds, WorkspaceRole? role = null) =>
        from t in db.StageTasks
        join l in db.StageTaskLots on t.LotId equals (Guid?)l.Id
        where l.AssigneeUserId == userId && (role == null || l.AssignedRole == role)
              && (seriesIds == null || seriesIds.Contains(t.SeriesId))
              && t.Status != StageTaskStatus.Approved && t.Status != StageTaskStatus.Cancelled
        select t;

    /// <summary>
    /// Thành viên rời Workspace: ô chưa nộp của họ về Unassigned để Producer giao lại; ô đã nộp/đang duyệt giữ nguyên
    /// để Editor vẫn chấm được bài (BR-01: không bỏ thành quả).
    /// </summary>
    public static async Task ReleaseMemberCellsAsync(IPanelForgeDbContext db, Guid userId, IReadOnlyCollection<Guid> seriesIds, CancellationToken ct)
    {
        var cells = await UnfinishedCellsOf(db, userId, seriesIds)
            .Where(t => t.Status == StageTaskStatus.Assigned || t.Status == StageTaskStatus.InProgress ||
                        t.Status == StageTaskStatus.ReworkRequired)
            .ToListAsync(ct);
        var lotIds = cells.Select(c => c.LotId).ToList();
        foreach (var c in cells) c.Unassign();
        await RefreshLotsAsync(db, lotIds, cells, ct);
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
