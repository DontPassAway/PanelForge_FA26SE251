using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Common;

/// <summary>Luật dùng chung của Create/Update Assignment (BR-03, BR-07, BR-12).</summary>
internal static class AssignmentRules
{
    /// <summary>Clean Cutover: chương đã có ô lưới V3 thì giao việc qua lưới, không tạo Assignment V2 nữa.</summary>
    public static async Task<string?> GridActiveReasonAsync(IPanelForgeDbContext db, Guid chapterId, CancellationToken ct) =>
        await db.StageTasks.AsNoTracking().AnyAsync(t => t.ChapterId == chapterId && t.Status != StageTaskStatus.Cancelled, ct)
            ? "Chương này đã dùng Lưới giao việc V3; hãy giao việc trên lưới."
            : null;

    /// <summary>Số việc chưa xong (V2 chưa Approved + ô lưới V3 chưa Approved/Cancelled) của một người trong các Series.</summary>
    public static async Task<int> OpenWorkCountAsync(
        IPanelForgeDbContext db, Guid userId, IReadOnlyCollection<Guid> seriesIds, CancellationToken ct) =>
        await db.Assignments.CountAsync(a => a.AssigneeUserId == userId && seriesIds.Contains(a.SeriesId)
                                             && a.Status != AssignmentStatus.Approved, ct)
        + await TaskGrid.Common.TaskGridRules.UnfinishedCellsOf(db, userId, seriesIds).CountAsync(ct);

    /// <summary>
    /// Thành viên rời Studio: huỷ việc V2 đang mở của họ nếu chưa có bài nộp và không việc nào phụ thuộc
    /// (cùng điều kiện với DeleteAssignment). Việc đã có bài nộp giữ lại để Producer đổi người nhận (BR-01).
    /// </summary>
    public static async Task ReleaseMemberAssignmentsAsync(
        IPanelForgeDbContext db, Guid userId, IReadOnlyCollection<Guid> seriesIds, CancellationToken ct)
    {
        var open = await db.Assignments
            .Where(a => a.AssigneeUserId == userId && seriesIds.Contains(a.SeriesId)
                        && (a.Status == AssignmentStatus.Assigned || a.Status == AssignmentStatus.InProgress
                            || a.Status == AssignmentStatus.ReworkRequired || a.Status == AssignmentStatus.Blocked))
            .ToListAsync(ct);
        if (open.Count == 0) return;

        var ids = open.Select(a => a.Id).ToList();
        var submitted = await db.Submissions.Where(s => ids.Contains(s.AssignmentId))
            .Select(s => s.AssignmentId).Distinct().ToListAsync(ct);
        var chapterIds = open.Select(a => a.ChapterId).Distinct().ToList();
        var dependedOn = (await db.Assignments.AsNoTracking().Where(a => chapterIds.Contains(a.ChapterId)).ToListAsync(ct))
            .Where(a => !ids.Contains(a.Id)).SelectMany(a => a.GetDependsOnAssignmentIds()).ToHashSet();

        foreach (var a in open.Where(a => !submitted.Contains(a.Id) && !dependedOn.Contains(a.Id)))
        {
            a.Cancel("Người nhận đã rời Studio.");
            db.Assignments.Remove(a); // xoá mềm (ISoftDelete)
        }
    }

    /// <summary>BR-12: Chapter đã Approved/Locked/Published thì không giao hay đổi việc.</summary>
    public static string? ChapterNotEditableReason(Chapter chapter) =>
        chapter.IsContentEditable
            ? null
            : $"Chapter đang ở trạng thái {chapter.Status}, không thể thay đổi việc.";

    /// <summary>
    /// Không xoá Page/Panel khi còn Assignment trỏ tới nó: đích của việc phải tồn tại, nếu không danh sách việc và tiến độ
    /// sẽ hỏi dữ liệu của một trang đã mất. Trả lý do chặn, hoặc null nếu xoá được.
    /// </summary>
    private static readonly StageTaskStatus[] NotStarted =
        [StageTaskStatus.Cancelled, StageTaskStatus.Unassigned, StageTaskStatus.Assigned];

    /// <summary>Xoá trang: huỷ các ô chưa bắt đầu của trang (gỡ khỏi lô trước), nếu không trang không bao giờ xoá được.</summary>
    public static async Task CancelUnstartedPageCellsAsync(IPanelForgeDbContext db, Guid pageId, CancellationToken ct)
    {
        var cells = await db.StageTasks
            .Where(t => t.PageId == pageId && (t.Status == StageTaskStatus.Unassigned || t.Status == StageTaskStatus.Assigned))
            .ToListAsync(ct);
        var lotIds = cells.Select(c => c.LotId).ToList();
        foreach (var c in cells)
        {
            if (c.Status == StageTaskStatus.Assigned) c.Unassign();
            c.Cancel();
        }
        await TaskGrid.Common.TaskGridRules.RefreshLotsAsync(db, lotIds, cells, ct);
    }

    public static async Task<string?> DeleteBlockedReasonAsync(
        IPanelForgeDbContext db, string what, IReadOnlyCollection<Guid> targetIds, CancellationToken ct)
    {
        var statuses = await db.Assignments.AsNoTracking()
            .Where(a => a.EntityType != WorkflowEntityType.Chapter && targetIds.Contains(a.TargetEntityId))
            .Select(a => a.Status).ToListAsync(ct);
        // Lưới V3: ô cấp Trang đã bắt đầu (có thành quả) chặn xoá. Ô chưa bắt đầu (Unassigned/Assigned) được huỷ cùng trang
        // ở CancelUnstartedPageCellsAsync. Ô không cấp Panel nên chỉ khớp id trang.
        var gridCells = await db.StageTasks.AsNoTracking()
            .CountAsync(t => t.PageId != null && targetIds.Contains(t.PageId.Value) && !NotStarted.Contains(t.Status), ct);
        if (gridCells > 0)
            return $"Không thể xoá {what}: đang có {gridCells} ô trên lưới giao việc đã bắt đầu làm hoặc đã duyệt.";
        if (statuses.Count == 0) return null;

        return statuses.Any(s => s == AssignmentStatus.Approved)
            ? $"Không thể xoá {what}: đang có {statuses.Count} việc được giao, trong đó có việc đã được duyệt (Approved) nên không huỷ được."
            : $"Không thể xoá {what}: đang có {statuses.Count} việc được giao. Producer cần huỷ các việc này trước.";
    }

    /// <summary>Người nhận thông báo "đã nộp": Editor của Workspace; nếu chưa có Editor thì Producer. Loại trừ người nộp.</summary>
    public static async Task<List<Guid>> ReviewerUserIdsAsync(
        IPanelForgeDbContext db, Guid workspaceId, Guid excludeUserId, CancellationToken ct)
    {
        async Task<List<Guid>> WithRole(WorkspaceRole role) => await db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && m.Role == role && m.UserId != excludeUserId)
            .Select(m => m.UserId).ToListAsync(ct);

        var editors = await WithRole(WorkspaceRole.Editor);
        return editors.Count > 0 ? editors : await WithRole(WorkspaceRole.Producer);
    }

    /// <summary>BR-07: người nhận phải là thành viên Workspace đã chấp nhận lời mời (Active); vai trò lấy từ membership, phải khớp stage.</summary>
    public static async Task<Result<WorkspaceRole>> ResolveAssigneeRoleAsync(
        IPanelForgeDbContext db, Guid workspaceId, Guid assigneeUserId, PipelineStage stage, CancellationToken ct)
    {
        if (assigneeUserId == Guid.Empty)
            return Result<WorkspaceRole>.Failure("Assignment phải có người nhận.", ResultErrorCodes.Validation);

        // Công đoạn kết thúc (vd "Approved") chỉ là trạng thái đã xong, không phải việc để giao; duyệt nằm ở approval gate.
        if (stage.IsTerminal)
            return Result<WorkspaceRole>.Failure(
                $"Công đoạn '{stage.Name}' là công đoạn kết thúc, không giao việc được. Hãy giao ở công đoạn duyệt trước đó.",
                ResultErrorCodes.Validation);

        var member = await db.WorkspaceMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == assigneeUserId, ct);
        if (member is null)
            return Result<WorkspaceRole>.Failure("Người nhận không phải thành viên của Workspace.", ResultErrorCodes.Validation);

        if (member.Status != WorkspaceMemberStatus.Active)
            return Result<WorkspaceRole>.Failure(
                "Người nhận chưa chấp nhận lời mời vào Workspace nên chưa thể nhận việc.", ResultErrorCodes.Validation);

        if (!stage.CanBeHeldBy(member.Role))
            return Result<WorkspaceRole>.Failure(
                $"Stage '{stage.Name}' dành cho vai trò {stage.AllowedRole}, người nhận là {member.Role}.", ResultErrorCodes.Validation);

        return Result<WorkspaceRole>.Success(member.Role);
    }

    /// <summary>
    /// Giao việc trong Series = người nhận thành thành viên Series (role = role Workspace), để quyền theo Series
    /// không chặn chính việc vừa giao. Từng bị gỡ thì thêm dòng mới (unique index chỉ áp cho dòng chưa xoá mềm);
    /// không tự xoá khi việc kết thúc. Lưu cùng SaveChanges của handler.
    /// </summary>
    public static async Task EnsureSeriesMemberAsync(
        IPanelForgeDbContext db, Guid seriesId, Guid userId, WorkspaceRole role, CancellationToken ct)
    {
        var existing = await db.SeriesMembers
            .FirstOrDefaultAsync(m => m.SeriesId == seriesId && m.UserId == userId && !m.IsDeleted, ct);

        if (existing is null) db.SeriesMembers.Add(SeriesMember.Create(seriesId, userId, role));
        else if (existing.Role != role) existing.ChangeRole(role);
    }

    /// <summary>
    /// Một phần nội dung chỉ thuộc một việc ở mỗi stage. Việc cấp Page phủ các Panel của trang đó. Tạo thêm việc chồng phạm vi
    /// luôn bị chặn, kể cả chồng lên việc cả chương. Riêng nghiệp vụ chuyển giao (<paramref name="transfer"/> = true, stage
    /// Artist/Letterer) được TÁCH trang/khung khỏi việc cả chương (việc cả chương mất phần đó, xem
    /// StageProgressRules.ResolvePanelsAsync), miễn việc cả chương còn đang làm (chưa nộp/duyệt). Trả lý do (409) hoặc null.
    /// </summary>
    public static async Task<string?> ScopeOverlapReasonAsync(
        IPanelForgeDbContext db, Guid chapterId, PipelineStage stage, WorkflowEntityType type, Guid targetId, CancellationToken ct,
        bool transfer = false)
    {
        var existing = await db.Assignments.AsNoTracking()
            .Where(a => a.ChapterId == chapterId && a.PipelineStageId == stage.Id)
            .Select(a => new { a.EntityType, a.TargetEntityId, a.AssignedRole, a.Status })
            .ToListAsync(ct);
        if (existing.Count == 0) return null;

        if (existing.Any(a => a.EntityType == type && a.TargetEntityId == targetId))
            return "Đối tượng này đã được giao việc ở stage này.";
        if (type == WorkflowEntityType.Chapter)
            return "Stage này đã có việc giao theo trang/khung trong chương; không thể giao thêm việc cho cả chương.";
        if (existing.FirstOrDefault(a => a.EntityType == WorkflowEntityType.Chapter) is { } whole)
        {
            if (!transfer && StageProgressRules.IsProductionRole(whole.AssignedRole))
                return "Phần này thuộc việc cả chương ở stage này. Dùng chuyển giao để chuyển trang/khung sang người khác.";
            if (!StageProgressRules.IsProductionRole(whole.AssignedRole))
                return "Stage này đã giao việc cho cả chương; không thể giao thêm việc theo trang/khung.";
            if (whole.Status is not (AssignmentStatus.Assigned or AssignmentStatus.InProgress or AssignmentStatus.ReworkRequired))
                return $"Việc cả chương ở stage này đang {whole.Status}; chỉ tách trang/khung khi việc đó còn đang làm.";
        }

        if (type == WorkflowEntityType.Page)
        {
            var assignedPanels = existing.Where(a => a.EntityType == WorkflowEntityType.Panel).Select(a => a.TargetEntityId).ToList();
            return assignedPanels.Count > 0 &&
                   await db.Panels.AsNoTracking().AnyAsync(p => p.PageId == targetId && assignedPanels.Contains(p.Id), ct)
                ? "Trang này đã có khung được giao việc ở stage này; không thể giao thêm việc cho cả trang."
                : null;
        }

        var assignedPages = existing.Where(a => a.EntityType == WorkflowEntityType.Page).Select(a => a.TargetEntityId).ToList();
        return assignedPages.Count > 0 &&
               await db.Panels.AsNoTracking().AnyAsync(p => p.Id == targetId && assignedPages.Contains(p.PageId), ct)
            ? "Trang chứa khung này đã được giao việc ở stage này."
            : null;
    }

    /// <summary>
    /// Kiểm tra đồ thị phụ thuộc đa nhiệm (DAG): mọi dependency phải thuộc cùng Chapter và không tạo chu trình (Cycle).
    /// </summary>
    public static async Task<string?> ValidateDependenciesAsync(
        IPanelForgeDbContext db, Guid chapterId, Guid? selfId, IEnumerable<Guid>? dependsOnIds, CancellationToken ct)
    {
        var depList = dependsOnIds?
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList() ?? [];

        if (depList.Count == 0) return null;

        if (selfId.HasValue && depList.Contains(selfId.Value))
            return "Assignment không thể phụ thuộc vào chính nó.";

        var assignmentsInChapter = await db.Assignments.AsNoTracking()
            .Where(a => a.ChapterId == chapterId)
            .ToListAsync(ct);

        var existingMap = assignmentsInChapter.ToDictionary(a => a.Id, a => a.GetDependsOnAssignmentIds());

        foreach (var depId in depList)
        {
            if (!existingMap.ContainsKey(depId))
                return "Assignment phụ thuộc không tồn tại trong Chapter này.";
        }

        if (selfId.HasValue)
        {
            var queue = new Queue<Guid>(depList);
            var visited = new HashSet<Guid>(depList);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current == selfId.Value)
                    return "Dependency tạo chu trình trong đồ thị công việc.";

                if (existingMap.TryGetValue(current, out var nextDeps))
                {
                    foreach (var next in nextDeps)
                    {
                        if (next == selfId.Value)
                            return "Dependency tạo chu trình trong đồ thị công việc.";

                        if (visited.Add(next))
                            queue.Enqueue(next);
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Backward compatibility cho dependency đơn.</summary>
    public static Task<string?> ValidateDependencyAsync(
        IPanelForgeDbContext db, Guid chapterId, Guid? selfId, Guid? dependsOnId, CancellationToken ct) =>
        ValidateDependenciesAsync(db, chapterId, selfId, dependsOnId.HasValue ? [dependsOnId.Value] : null, ct);
}
