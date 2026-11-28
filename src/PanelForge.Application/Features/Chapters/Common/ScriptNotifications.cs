using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Features.SeriesMembers.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Common;

/// <summary>
/// D12: báo Writer những gì xảy ra với kịch bản họ đã gửi (góp ý mới, bắt đầu sản xuất, mở lại).
/// Người nhận: Writer đã gửi duyệt nếu có, cộng mọi Writer của Workspace (studio thường chỉ một Writer); trừ người gây ra.
/// </summary>
internal static class ScriptNotifications
{
    public static async Task NotifyWritersAsync(
        IPanelForgeDbContext db, Chapter chapter, Guid actorUserId, string title, string message, CancellationToken ct,
        NotificationType type = NotificationType.AssignmentUpdated)
    {
        var workspaceId = await db.Series.AsNoTracking()
            .Where(s => s.Id == chapter.SeriesId).Select(s => (Guid?)s.WorkspaceId).FirstOrDefaultAsync(ct);
        if (workspaceId is null) return;

        // D10: Writer của Series
        var writers = await SeriesRoles.UserIdsWithRoleAsync(db, workspaceId.Value, chapter.SeriesId, WorkspaceRole.Writer, ct);
        if (chapter.ScriptSubmittedById is { } submitter) writers.Add(submitter);

        foreach (var userId in writers.Where(id => id != actorUserId).Distinct())
        {
            db.Notifications.Add(Notification.Create(
                userId, type, title, message, chapterId: chapter.Id));
        }
    }
}
