using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Features.SeriesMembers.Common;
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
    /// để Editor vẫn chấm được bài (BR-01: không bỏ thành quả). Báo Owner, Producer, Editor các chương còn sửa được để giao lại.
    /// </summary>
    public static async Task ReleaseMemberCellsAsync(
        IPanelForgeDbContext db, Guid userId, Guid workspaceId, Guid ownerId, IReadOnlyCollection<Guid> seriesIds, CancellationToken ct)
    {
        // N5: làm trước đoạn thoát sớm, vì Editor thường không giữ lô nào
        await ReleaseReviewerAsync(db, userId, seriesIds, ct);
        var cells = await UnfinishedCellsOf(db, userId, seriesIds)
            .Where(t => t.Status == StageTaskStatus.Assigned || t.Status == StageTaskStatus.InProgress ||
                        t.Status == StageTaskStatus.ReworkRequired)
            .ToListAsync(ct);
        var lotIds = cells.Select(c => c.LotId).ToList();
        foreach (var c in cells) c.Unassign();
        await RefreshLotsAsync(db, lotIds, cells, ct);
        if (cells.Count == 0) return;

        // D10: Producer của Studio + Editor của các Series liên quan
        var seriesEditors = await db.SeriesMembers.AsNoTracking()
            .Where(sm => seriesIds.Contains(sm.SeriesId) && !sm.IsDeleted && sm.Role == WorkspaceRole.Editor
                         && db.WorkspaceMembers.Any(m => m.WorkspaceId == workspaceId && m.UserId == sm.UserId
                                                         && m.Status == WorkspaceMemberStatus.Active))
            .Select(sm => sm.UserId).ToListAsync(ct);
        var recipients = (await SeriesRoles.StudioProducerIdsAsync(db, workspaceId, ct)).Concat(seriesEditors)
            .Append(ownerId).Where(id => id != userId).Distinct().ToList();
        var chapterIds = cells.Select(c => c.ChapterId).Distinct().ToList();
        var chapters = await db.Chapters.AsNoTracking().Where(c => chapterIds.Contains(c.Id)).ToListAsync(ct);
        foreach (var chapter in chapters.Where(c => c.IsContentEditable))
            foreach (var recipient in recipients)
                db.Notifications.Add(TaskGridNotifications.NeedsReassign(
                    recipient, chapter.Id, chapter.ChapterNumber, cells.Count(c => c.ChapterId == chapter.Id)));
    }

    /// <summary>
    /// N5: người dùng thôi là Editor (rời Studio hoặc đổi vai trò). Bỏ gợi ý người duyệt trên ô Submitted và trả ô
    /// UnderReview của họ về hàng đợi Submitted để Editor khác nhận.
    /// </summary>
    public static async Task ReleaseReviewerAsync(IPanelForgeDbContext db, Guid userId, IReadOnlyCollection<Guid> seriesIds, CancellationToken ct)
    {
        var cells = await db.StageTasks
            .Where(t => t.ReviewerUserId == userId && seriesIds.Contains(t.SeriesId)
                        && (t.Status == StageTaskStatus.Submitted || t.Status == StageTaskStatus.UnderReview))
            .ToListAsync(ct);
        foreach (var c in cells)
        {
            if (c.Status == StageTaskStatus.UnderReview) c.ReleaseReview();
            else c.ClearSuggestedReviewer();
        }
    }

    /// <summary>
    /// R2: gợi ý một người duyệt cho mỗi ô vừa nộp (đang Submitted). Ứng viên là Editor Active, không có thì Producer Active
    /// và Owner; luôn khác người nộp (BR-06; Submit chỉ nhận ô của chính người nộp nên đó cũng là người giữ lô).
    /// Chọn người đang có ít ô Submitted + UnderReview nhất, tính cả các ô vừa gợi ý trong lần này.
    /// Trả false khi không có ứng viên (giữ cách báo cũ).
    /// </summary>
    public static async Task<bool> SuggestReviewersAsync(
        IPanelForgeDbContext db, Guid workspaceId, Guid ownerId, Guid submitterId, IReadOnlyList<StageTask> cells, CancellationToken ct)
    {
        if (cells.Count == 0) return false;
        // D10: Editor của Series chứa các ô; chưa có thì Producer của Studio
        var chapterId = cells[0].ChapterId;
        var seriesId = await db.Chapters.AsNoTracking().Where(c => c.Id == chapterId).Select(c => c.SeriesId).FirstAsync(ct);
        var candidates = (await SeriesRoles.UserIdsWithRoleAsync(db, workspaceId, seriesId, WorkspaceRole.Editor, ct))
            .Where(id => id != submitterId).ToList();
        if (candidates.Count == 0)
            candidates = (await SeriesRoles.StudioProducerIdsAsync(db, workspaceId, ct)).Append(ownerId)
                .Where(id => id != submitterId && id != Guid.Empty).Distinct().ToList();
        if (candidates.Count == 0) return false;

        var cellIds = cells.Select(c => c.Id).ToList();
        var counts = await db.StageTasks.AsNoTracking()
            .Where(t => t.ReviewerUserId != null && candidates.Contains(t.ReviewerUserId.Value) && !cellIds.Contains(t.Id)
                        && (t.Status == StageTaskStatus.Submitted || t.Status == StageTaskStatus.UnderReview))
            .GroupBy(t => t.ReviewerUserId!.Value).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var load = candidates.ToDictionary(id => id, id => counts.GetValueOrDefault(id));

        foreach (var cell in cells)
        {
            // ponytail: hoà tải thì lấy theo Guid cho ổn định; cần công bằng hơn thì thêm mốc "lần cuối được gợi ý"
            var pick = load.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
            cell.SuggestReviewer(pick);
            load[pick]++;
        }
        return true;
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
