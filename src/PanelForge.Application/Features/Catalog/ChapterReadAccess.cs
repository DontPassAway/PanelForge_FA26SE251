using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Catalog;

/// <summary>
/// Quyền đọc nội dung chương trả phí (<c>Chapter.IsLocked</c>) trên các API catalog công khai.
/// Được đọc khi: Publisher/Admin hệ thống, thành viên đang hoạt động của Studio sở hữu truyện,
/// VIP còn hạn, hoặc đã có <c>ChapterEntitlement</c>. Khách chưa đăng nhập không bao giờ đọc được chương khóa.
/// Một nguồn duy nhất cho danh sách chương, nội dung chương và ảnh trang để ba nơi không lệch nhau.
/// </summary>
public static class ChapterReadAccess
{
    public const string LockedMessage =
        "Chương truyện trả phí. Vui lòng đăng nhập và mở khóa chương (bằng Ví, VietQR hoặc gói VIP) để đọc.";

    /// <summary>Người xem có quyền đọc mọi chương của Studio mà không cần mua (Publisher/Admin, thành viên Studio).</summary>
    public static async Task<bool> IsStaffAsync(
        IPanelForgeDbContext db, Guid? userId, Guid workspaceId, CancellationToken ct)
    {
        if (!userId.HasValue) return false;

        var systemRole = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId.Value && u.IsActive)
            .Select(u => (SystemRole?)u.Role)
            .FirstOrDefaultAsync(ct);
        if (systemRole is SystemRole.Publisher or SystemRole.Admin) return true;

        return await db.WorkspaceMembers.AsNoTracking()
            .AnyAsync(m => m.UserId == userId.Value
                           && m.WorkspaceId == workspaceId
                           && m.Status == WorkspaceMemberStatus.Active, ct);
    }

    public static Task<bool> HasActiveVipAsync(IPanelForgeDbContext db, Guid userId, DateTime now, CancellationToken ct) =>
        db.ReaderWallets.AsNoTracking()
            .AnyAsync(w => w.UserId == userId && !w.IsDeleted && w.IsVip
                           && (w.VipExpiresAt == null || w.VipExpiresAt > now), ct);

    /// <summary>Có được đọc một chương cụ thể không (chương không khóa luôn đọc được).</summary>
    public static async Task<bool> CanReadAsync(
        IPanelForgeDbContext db, Guid? userId, Guid chapterId, Guid workspaceId, bool isLocked, CancellationToken ct)
    {
        if (!isLocked) return true;
        if (!userId.HasValue) return false;
        if (await IsStaffAsync(db, userId, workspaceId, ct)) return true;
        if (await HasActiveVipAsync(db, userId.Value, DateTime.UtcNow, ct)) return true;

        return await db.ChapterEntitlements.AsNoTracking()
            .AnyAsync(e => e.UserId == userId.Value && e.ChapterId == chapterId && !e.IsDeleted, ct);
    }
}
