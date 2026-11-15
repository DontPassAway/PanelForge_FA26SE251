using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Common;

/// <summary>
/// Quyền ghi canvas / bố cục Panel theo phạm vi việc của Artist/Letterer (cùng luật với tải artwork). Panel đang có việc mở
/// (chưa duyệt) của vai trò người sửa thì chỉ người giữ việc đó, ở trạng thái đang làm, mới ghi được; đã nộp chờ duyệt thì
/// không ai ghi (giữ đúng bản đã nộp). Ghi cả trang (element không gắn Panel, thêm Panel) cần giữ MỌI việc mở trên trang, để
/// không đè phần đã chuyển giao cho người khác. Chưa có việc mở nào phủ phần đó thì giữ hành vi cũ (theo vai trò Workspace).
/// Producer luôn được; vai trò khác (Writer, Editor) do luật riêng của chúng.
/// </summary>
internal static class WorkScopeGuard
{
    private static readonly AssignmentStatus[] Working =
        [AssignmentStatus.Assigned, AssignmentStatus.InProgress, AssignmentStatus.ReworkRequired];

    private static readonly AssignmentStatus[] Open =
        [.. Working, AssignmentStatus.Submitted, AssignmentStatus.UnderReview];

    private static readonly StageTaskStatus[] GridWorking = [StageTaskStatus.InProgress, StageTaskStatus.ReworkRequired];

    private static readonly StageTaskStatus[] GridOpen =
        [StageTaskStatus.Assigned, StageTaskStatus.InProgress, StageTaskStatus.Submitted, StageTaskStatus.UnderReview, StageTaskStatus.ReworkRequired];

    /// <summary>Lý do chặn (trả 403), hoặc null nếu được ghi. <paramref name="panelId"/> null = ghi cả trang.</summary>
    public static async Task<string?> DenyReasonAsync(
        IPanelForgeDbContext db, Guid userId, Guid pageId, Guid? panelId, CancellationToken ct)
    {
        var chapterId = await db.Pages.AsNoTracking().Where(p => p.Id == pageId).Select(p => (Guid?)p.ChapterId).FirstOrDefaultAsync(ct);
        if (chapterId is null) return null; // handler tự báo không tồn tại

        var seriesId = await db.Chapters.AsNoTracking().Where(c => c.Id == chapterId).Select(c => (Guid?)c.SeriesId).FirstOrDefaultAsync(ct);
        if (seriesId is null) return null;
        var workspaceId = await db.Series.AsNoTracking().Where(s => s.Id == seriesId).Select(s => s.WorkspaceId).FirstOrDefaultAsync(ct);
        var role = await db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && m.UserId == userId).Select(m => (WorkspaceRole?)m.Role).FirstOrDefaultAsync(ct);
        if (role is not { } r || !StageProgressRules.IsProductionRole(r)) return null;

        // Lưới V3: trang có ô đang mở (đã giao, chưa duyệt) của vai trò này thì chỉ người giữ ô, ở trạng thái làm việc và đầu vào
        // còn hiệu lực, mới ghi được. Ô lưới là cấp Trang nên ghi panel hay cả trang đều xét theo trang.
        if (await GridDenyReasonAsync(db, userId, r, chapterId.Value, pageId, ct) is { } grid)
            return grid.Length == 0 ? null : grid;

        var targetPanels = panelId is { } pid
            ? [pid]
            : await db.Panels.AsNoTracking().Where(p => p.PageId == pageId).Select(p => p.Id).ToListAsync(ct);

        var open = await db.Assignments
            .Where(a => a.ChapterId == chapterId && a.AssignedRole == r && Open.Contains(a.Status)).ToListAsync(ct);
        var covering = new List<(Guid Assignee, AssignmentStatus Status)>();
        foreach (var a in open)
        {
            var covers = targetPanels.Count == 0
                ? a.EntityType == WorkflowEntityType.Chapter || (a.EntityType == WorkflowEntityType.Page && a.TargetEntityId == pageId)
                : (await StageProgressRules.ResolvePanelsAsync(db, a, ct)).Any(p => targetPanels.Contains(p.Id));
            if (covers) covering.Add((a.AssigneeUserId, a.Status));
        }

        if (covering.Count == 0) return null;

        bool Mine((Guid Assignee, AssignmentStatus Status) c) => c.Assignee == userId && Working.Contains(c.Status);
        var allowed = panelId is null ? covering.All(Mine) : covering.Any(Mine);
        if (allowed) return null;

        if (covering.Any(c => c.Assignee == userId))
            return "Phần này đang chờ duyệt hoặc không còn ở trạng thái làm việc của bạn; không sửa được nội dung đã nộp.";
        return panelId is null
            ? "Trang này có phần đang giao cho người khác; chỉ sửa được khung thuộc việc của bạn."
            : "Khung này thuộc việc của người khác; bạn không có quyền sửa.";
    }

    /// <summary>null = lưới không phủ trang này (xét luật cũ); "" = cho ghi; còn lại = lý do chặn.</summary>
    private static async Task<string?> GridDenyReasonAsync(
        IPanelForgeDbContext db, Guid userId, WorkspaceRole role, Guid chapterId, Guid pageId, CancellationToken ct)
    {
        var cells = await (from t in db.StageTasks.AsNoTracking()
                           join l in db.StageTaskLots.AsNoTracking() on t.LotId equals (Guid?)l.Id
                           where t.ChapterId == chapterId && l.AssignedRole == role
                                 && (t.PageId == pageId || t.PageId == null) && GridOpen.Contains(t.Status)
                           select new { t.Status, t.IsInputOutdated, l.AssigneeUserId }).ToListAsync(ct);
        if (cells.Count == 0)
        {
            // Chương đã dùng lưới: không còn ô mở của vai trò này trên trang (chưa giao hoặc đã duyệt) thì từ chối, không rơi
            // về luật V2 (bảng Assignments rỗng → mở cho mọi Artist/Letterer sửa trang đã duyệt).
            var usesGrid = await db.StageTasks.AsNoTracking()
                .AnyAsync(t => t.ChapterId == chapterId && t.LotId != null && t.Status != StageTaskStatus.Cancelled, ct);
            return usesGrid ? "Trang này không có ô việc đang mở của bạn trên lưới giao việc (chưa giao hoặc đã duyệt); không sửa được." : null;
        }

        var mine = cells.Where(c => c.AssigneeUserId == userId).ToList();
        if (mine.Any(c => GridWorking.Contains(c.Status) && !c.IsInputOutdated)) return "";
        if (mine.Count == 0) return "Trang này đang giao cho người khác trên lưới giao việc; bạn không có quyền sửa.";
        if (mine.Any(c => c.IsInputOutdated)) return "Đầu vào của ô việc đã lỗi thời, chờ Producer xử lý Impact Review trước khi sửa tiếp.";
        if (mine.Any(c => c.Status == StageTaskStatus.Assigned)) return "Ô việc chưa bắt đầu: bấm Bắt đầu khi công đoạn trước đã được duyệt.";
        return "Phần này đang chờ duyệt; không sửa được nội dung đã nộp.";
    }
}
