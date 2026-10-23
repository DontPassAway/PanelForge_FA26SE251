using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Scenes.Common;

/// <summary>
/// Cùng cổng giao việc với ScriptLine: Chapter đã giao việc cho vai trò của người sửa thì chỉ người được giao (đang làm việc)
/// mới tạo/sửa/xoá/sắp xếp Scene. Producer luôn được. Trả lý do chặn (403) hoặc null.
/// </summary>
internal static class SceneAssignmentGate
{
    public static async Task<string?> BlockedReasonAsync(
        IPanelForgeDbContext db, IWorkspaceAuthorizationService auth, Guid userId, Guid chapterId, CancellationToken ct)
    {
        var chapter = await db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == chapterId, ct);
        if (chapter is null) return null; // handler đã báo NotFound

        var workspaceId = await db.Series.AsNoTracking().Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstOrDefaultAsync(ct);
        var role = await auth.GetMemberRoleAsync(userId, workspaceId, ct);
        if (role is null or WorkspaceRole.Producer) return null;

        if (chapter.IsScriptFrozen)
            return "Kịch bản của chương này đã được đóng băng (Frozen / Read-only) để đưa vào sản xuất hình ảnh. Producer cần mở lại kịch bản nếu muốn sửa đổi.";

        // V2.5: Writer chủ động tạo và sửa phân cảnh tiền kỳ mà không cần assignment
        if (role == WorkspaceRole.Writer) return null;

        return (await AiWorkflowFacts.EvaluateAssignmentAsync(db, userId, chapter, role.Value, ct)).BlockedReason;
    }
}
