using Microsoft.EntityFrameworkCore;
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
        IPanelForgeDbContext db, Chapter chapter, Guid actorUserId, string title, string message, CancellationToken ct)
    {
        var workspaceId = await db.Series.AsNoTracking()
            .Where(s => s.Id == chapter.SeriesId).Select(s => (Guid?)s.WorkspaceId).FirstOrDefaultAsync(ct);
        if (workspaceId is null) return;

        var writers = await db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && m.Role == WorkspaceRole.Writer)
            .Select(m => m.UserId).ToListAsync(ct);
        if (chapter.ScriptSubmittedById is { } submitter) writers.Add(submitter);

        foreach (var userId in writers.Where(id => id != actorUserId).Distinct())
        {
            db.Notifications.Add(Notification.Create(
                userId, NotificationType.AssignmentUpdated, title, message, chapterId: chapter.Id));
        }
    }
}
