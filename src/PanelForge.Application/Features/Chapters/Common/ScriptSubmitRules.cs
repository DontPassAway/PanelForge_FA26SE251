using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Features.SeriesMembers.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Common;

/// <summary>Bước chuyển trạng thái "gửi duyệt kịch bản" (Draft → InReview) dùng chung cho SubmitScript và SubmitChapterScript.</summary>
internal static class ScriptSubmitRules
{
    /// <summary>Đánh dấu chương và phiên bản đã gửi, báo Editor và Producer. Chỉ thêm vào DbContext; người gọi SaveChanges. Trả lỗi 409 hoặc null.</summary>
    public static async Task<string?> ApplySubmitAsync(
        IPanelForgeDbContext db, Chapter chapter, ScriptVersion version, Guid userId, Guid workspaceId, CancellationToken ct)
    {
        try
        {
            chapter.SubmitScript(userId, version.Id);
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
        version.MarkSubmitted(userId);
        chapter.UpdatedBy = userId.ToString();

        // D10: Editor của Series + Producer của Studio
        var recipients = (await SeriesRoles.UserIdsWithRoleAsync(db, workspaceId, chapter.SeriesId, WorkspaceRole.Editor, ct))
            .Concat(await SeriesRoles.StudioProducerIdsAsync(db, workspaceId, ct))
            .Where(id => id != userId).Distinct().ToList();
        foreach (var recipientId in recipients)
        {
            db.Notifications.Add(Notification.Create(
                recipientId,
                NotificationType.AssignmentSubmitted,
                $"Kịch bản Chương {chapter.ChapterNumber} chờ duyệt",
                $"Writer đã gửi duyệt kịch bản Chương {chapter.ChapterNumber}. Editor góp ý theo dòng; Producer bấm Bắt đầu sản xuất khi sẵn sàng.",
                chapterId: chapter.Id));
        }
        return null;
    }
}
