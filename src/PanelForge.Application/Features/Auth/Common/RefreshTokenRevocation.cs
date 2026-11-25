using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Audit;

namespace PanelForge.Application.Features.Auth.Common;

internal static class RefreshTokenRevocation
{
    /// <summary>
    /// Thu hồi mọi refresh token còn hiệu lực của user (đổi/đặt lại mật khẩu, nghi bị lộ token), trừ token có hash
    /// <paramref name="exceptTokenHash"/> nếu có (phiên hiện tại). Chỉ đánh dấu, chưa lưu.
    /// </summary>
    public static async Task RevokeAllAsync(
        IPanelForgeDbContext dbContext, Guid userId, CancellationToken cancellationToken, string? exceptTokenHash = null,
        string? reason = null)
    {
        var active = await dbContext.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && (exceptTokenHash == null || t.TokenHash != exceptTokenHash))
            .ToListAsync(cancellationToken);
        foreach (var token in active)
            token.Revoke();

        if (active.Count > 0)
            dbContext.AuditLogs.Add(AuditLog.Create(
                action: "SESSIONS_REVOKED", entityName: "Session", userId: userId,
                details: $"Thu hồi {active.Count} phiên đăng nhập{(reason is null ? "" : $": {reason}")}."));
    }
}
