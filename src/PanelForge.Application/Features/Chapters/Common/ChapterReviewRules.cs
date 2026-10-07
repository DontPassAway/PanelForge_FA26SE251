using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Common;

/// <summary>Truy vấn và thông báo dùng chung của luồng duyệt / phát hành Chapter (họp tuần 5).</summary>
internal static class ChapterReviewRules
{
    public static async Task<Result<(Chapter Chapter, Guid WorkspaceId)>> LoadAsync(
        IPanelForgeDbContext db, Guid chapterId, ChapterStatus requiredStatus, CancellationToken ct)
    {
        var chapter = await db.Chapters.FirstOrDefaultAsync(c => c.Id == chapterId, ct);
        if (chapter is null)
            return Result<(Chapter, Guid)>.Failure($"Chapter {chapterId} không tồn tại.", ResultErrorCodes.NotFound);

        if (chapter.Status != requiredStatus)
            return Result<(Chapter, Guid)>.Failure(
                $"Chapter đang ở trạng thái {chapter.Status}, thao tác này cần {requiredStatus}.", ResultErrorCodes.Conflict);

        var workspaceId = await db.Series.AsNoTracking()
            .Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstAsync(ct);
        return Result<(Chapter, Guid)>.Success((chapter, workspaceId));
    }

    public static async Task<List<Guid>> MemberIdsAsync(
        IPanelForgeDbContext db, Guid workspaceId, WorkspaceRole role, Guid excludeUserId, CancellationToken ct) =>
        await db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && m.Role == role && m.UserId != excludeUserId)
            .Select(m => m.UserId).ToListAsync(ct);

    public static string Label(Chapter c) => c.Title is { Length: > 0 } t ? $"Chương {c.ChapterNumber}: {t}" : $"Chương {c.ChapterNumber}";

    public static Notification Notify(Guid userId, NotificationType type, string title, Chapter c, string? detail = null) =>
        Notification.Create(userId, type, title, detail is null ? Label(c) : $"{Label(c)} — {detail}", chapterId: c.Id);
}
