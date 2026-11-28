using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Bible.Common;

/// <summary>
/// Thông báo góp ý concept Bible (Q20). Người nhận: Artist đã nộp concept đang được góp ý, cộng những người đã
/// tham gia luồng (người viết góp ý gốc và các trả lời). Luôn trừ người gây ra.
/// </summary>
internal static class ConceptCommentNotifications
{
    /// <summary>Artist đã nộp concept gần nhất của revision (SubmitConcept ghi ArtistUserId).</summary>
    public static Task<Guid?> ArtistOfRevisionAsync(IPanelForgeDbContext db, Guid revisionId, CancellationToken ct) =>
        db.BibleEntryRevisions.AsNoTracking()
            .Where(r => r.Id == revisionId).Select(r => r.ArtistUserId).FirstOrDefaultAsync(ct);

    /// <summary>Góp ý mới trên concept: báo Artist đã nộp concept (trừ khi chính họ viết).</summary>
    public static void NotifyArtist(IPanelForgeDbContext db, Guid? artistId, Guid actorUserId, string message)
    {
        if (artistId is not { } artist || artist == actorUserId) return;
        db.Notifications.Add(Notification.Create(
            artist, NotificationType.ConceptCommentAdded, "Có góp ý mới trên concept của bạn", Preview(message)));
    }

    /// <summary>Trả lời: báo Artist và mọi người trong luồng (gốc + các trả lời), trừ người vừa trả lời.</summary>
    public static async Task NotifyThreadAsync(
        IPanelForgeDbContext db, Guid? artistId, Guid rootCommentId, Guid actorUserId, string message, CancellationToken ct)
    {
        var recipients = await db.BibleConceptComments.AsNoTracking()
            .Where(c => !c.IsDeleted && (c.Id == rootCommentId || c.ParentCommentId == rootCommentId))
            .Select(c => c.AuthorId)
            .ToListAsync(ct);

        var set = new HashSet<Guid>(recipients);
        if (artistId is { } artist) set.Add(artist);
        set.Remove(actorUserId);

        foreach (var userId in set)
        {
            db.Notifications.Add(Notification.Create(
                userId, NotificationType.ConceptCommentReplied, "Có trả lời mới trong góp ý concept", Preview(message)));
        }
    }

    /// <summary>Rút gọn nội dung góp ý cho thông báo (tối đa 120 ký tự).</summary>
    private static string Preview(string message) => message.Length > 120 ? message[..120] + "…" : message;
}
