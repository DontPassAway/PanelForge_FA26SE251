using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.API.Authentication;

/// <summary>
/// Kiểm tra access JWT ở mỗi request (một truy vấn): user còn hoạt động và phiên đăng nhập của token chưa bị
/// thu hồi / hết hạn. Nhờ vậy đăng xuất, Producer thu hồi phiên, đổi mật khẩu… có hiệu lực ngay, không đợi JWT hết hạn.
/// </summary>
public static class AccessTokenGuard
{
    /// <param name="sessionId">Claim <c>session_id</c>; null với token phát trước khi có claim (chỉ kiểm tra user).</param>
    public static Task<bool> IsValidAsync(
        IPanelForgeDbContext db, Guid userId, Guid? sessionId, DateTime now, CancellationToken ct)
    {
        var refreshTokens = db.RefreshTokens;
        return db.Users.AnyAsync(u => u.Id == userId && u.IsActive
            && (sessionId == null || refreshTokens.Any(t =>
                t.SessionId == sessionId && t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)), ct);
    }
}
