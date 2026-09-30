using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Auth.Common;

internal static class RefreshTokenRevocation
{
    /// <summary>Thu hồi mọi refresh token còn hiệu lực của user (đổi/đặt lại mật khẩu, nghi bị lộ token). Chỉ đánh dấu, chưa lưu.</summary>
    public static async Task RevokeAllAsync(IPanelForgeDbContext dbContext, Guid userId, CancellationToken cancellationToken)
    {
        var active = await dbContext.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in active)
            token.Revoke();
    }
}
