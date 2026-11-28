using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.SeriesMembers.Common;
using PanelForge.Domain.Entities.Preview;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.PageComments.Common;

/// <summary>
/// Thông báo góp ý ghim trên trang (Q31). Người xem được góp ý: Editor và Writer của Series, cộng Producer của Studio;
/// trừ người viết. Resolve/Reopen báo người viết góp ý (nếu không phải chính họ thao tác).
/// </summary>
internal static class PageCommentNotifications
{
    public static async Task NotifyViewersAsync(IPanelForgeDbContext db, PageComment comment, CancellationToken ct)
    {
        var workspaceId = await db.Series.AsNoTracking()
            .Where(s => s.Id == comment.SeriesId).Select(s => (Guid?)s.WorkspaceId).FirstOrDefaultAsync(ct);
        if (workspaceId is null) return;

        var recipients = new HashSet<Guid>();
        recipients.UnionWith(await SeriesRoles.UserIdsWithRoleAsync(db, workspaceId.Value, comment.SeriesId, WorkspaceRole.Editor, ct));
        recipients.UnionWith(await SeriesRoles.UserIdsWithRoleAsync(db, workspaceId.Value, comment.SeriesId, WorkspaceRole.Writer, ct));
        recipients.UnionWith(await SeriesRoles.StudioProducerIdsAsync(db, workspaceId.Value, ct));
        recipients.Remove(comment.AuthorUserId);

        foreach (var userId in recipients)
        {
            db.Notifications.Add(Notification.Create(
                userId, NotificationType.PageCommentAdded, "Góp ý mới trên trang", Preview(comment.Message),
                chapterId: comment.ChapterId));
        }
    }

    public static void NotifyAuthor(
        IPanelForgeDbContext db, PageComment comment, Guid actorUserId, NotificationType type, string title)
    {
        if (comment.AuthorUserId == actorUserId) return;
        db.Notifications.Add(Notification.Create(
            comment.AuthorUserId, type, title, Preview(comment.Message), chapterId: comment.ChapterId));
    }

    /// <summary>Rút gọn nội dung góp ý cho thông báo (tối đa 120 ký tự).</summary>
    private static string Preview(string message) => message.Length > 120 ? message[..120] + "…" : message;
}
