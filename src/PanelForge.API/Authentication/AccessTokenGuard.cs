using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Auth.Common;

namespace PanelForge.API.Authentication;

/// <summary>
/// Kiểm tra access JWT ở mỗi request: user còn hoạt động, phiên đăng nhập của token chưa bị thu hồi / hết hạn và
/// chưa quá idle timeout của session policy (V1.2). Nhờ vậy đăng xuất, Producer thu hồi phiên, đổi mật khẩu, bỏ máy
/// không dùng… có hiệu lực ngay, không đợi JWT hết hạn.
/// </summary>
public static class AccessTokenGuard
{
    /// <summary>Ghi LastActiveAt tối đa một lần mỗi khoảng này cho mỗi phiên: phiên có thể bị coi là idle sớm tối đa 30 giây.</summary>
    public static readonly TimeSpan ActivityWriteInterval = TimeSpan.FromSeconds(30);

    /// <param name="sessionId">Claim <c>session_id</c>; null với token phát trước khi có claim (chỉ kiểm tra user).</param>
    public static async Task<bool> IsValidAsync(
        IPanelForgeDbContext db, Guid userId, Guid? sessionId, DateTime now, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId && u.IsActive, ct)) return false;
        if (sessionId is null) return true;

        var token = await db.RefreshTokens
            .Where(t => t.SessionId == sessionId && t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .OrderByDescending(t => t.LastActiveAt)
            .FirstOrDefaultAsync(ct);
        if (token is null) return false;

        var policy = await SessionPolicy.ForUserAsync(db, userId, ct);
        if (policy.IsIdle(token.LastActiveAt, now)) return false;

        if (now - token.LastActiveAt >= ActivityWriteInterval)
        {
            token.MarkActive(now);
            await db.SaveChangesAsync(ct);
        }
        return true;
    }
}
