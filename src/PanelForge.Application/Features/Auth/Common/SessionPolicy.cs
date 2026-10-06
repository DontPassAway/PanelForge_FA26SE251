using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Auth.Common;

/// <summary>
/// Session policy hiệu lực của một user (V1.2): Administrator đặt theo Workspace, user thuộc nhiều Workspace chịu giá trị CHẶT NHẤT
/// (nhỏ nhất) trong các Workspace đó. Không thuộc Workspace nào (kể cả Admin) = không bị giới hạn thêm ngoài hạn refresh token mặc định.
/// Idle timeout chỉ kiểm tra khi làm mới token nên độ chính xác bằng hạn access token (JWT).
/// </summary>
internal sealed record SessionPolicy(TimeSpan? Lifetime, TimeSpan? IdleTimeout, int? MaxSessions)
{
    public static async Task<SessionPolicy> ForUserAsync(IPanelForgeDbContext db, Guid userId, CancellationToken ct)
    {
        var limits = await db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => new
            {
                m.Workspace.SessionLifetimeMinutes,
                m.Workspace.SessionIdleTimeoutMinutes,
                m.Workspace.MaxConcurrentSessions
            })
            .ToListAsync(ct);

        return new SessionPolicy(
            Minutes(limits.Select(l => l.SessionLifetimeMinutes).Min()),
            Minutes(limits.Select(l => l.SessionIdleTimeoutMinutes).Min()),
            limits.Select(l => l.MaxConcurrentSessions).Min());
    }

    private static TimeSpan? Minutes(int? minutes) => minutes.HasValue ? TimeSpan.FromMinutes(minutes.Value) : null;

    /// <summary>Hạn của refresh token: tối đa hạn mặc định, và không quá thời gian sống của phiên tính từ lúc bắt đầu phiên.</summary>
    public DateTime ExpiryFor(DateTime sessionStartedAt, DateTime now)
    {
        var expiry = now.Add(AuthSecrets.RefreshTokenLifetime);
        return Lifetime.HasValue ? Min(expiry, sessionStartedAt.Add(Lifetime.Value)) : expiry;
    }

    public bool IsSessionExpired(DateTime sessionStartedAt, DateTime now)
        => Lifetime.HasValue && now >= sessionStartedAt.Add(Lifetime.Value);

    /// <param name="lastIssuedAt">Lúc phát token hiện tại = lần làm mới gần nhất của phiên.</param>
    public bool IsIdle(DateTime lastIssuedAt, DateTime now)
        => IdleTimeout.HasValue && now - lastIssuedAt > IdleTimeout.Value;

    /// <summary>
    /// Phiên mới sắp được thêm: nếu vượt số phiên đồng thời thì thu hồi các phiên CŨ NHẤT cho vừa giới hạn.
    /// Phiên đã quá lifetime/idle không tính là đang hoạt động. Chỉ đánh dấu, chưa lưu.
    /// </summary>
    public async Task EnforceMaxSessionsAsync(IPanelForgeDbContext db, Guid userId, Guid newSessionId, DateTime now, CancellationToken ct)
    {
        if (MaxSessions is not { } max) return;

        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now && t.SessionId != newSessionId)
            .ToListAsync(ct);

        var sessions = tokens
            .Where(t => !IsSessionExpired(t.SessionStartedAt, now) && !IsIdle(t.CreatedAt, now))
            .GroupBy(t => t.SessionId)
            .OrderBy(g => g.Min(t => t.SessionStartedAt))
            .ToList();

        foreach (var session in sessions.Take(Math.Max(0, sessions.Count + 1 - max)))
            foreach (var token in session)
                token.Revoke();
    }

    private static DateTime Min(DateTime a, DateTime b) => a <= b ? a : b;
}
