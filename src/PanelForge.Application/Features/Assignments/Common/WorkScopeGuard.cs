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
}
