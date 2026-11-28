using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Admin.Models;
using PanelForge.Application.Features.Audit.Common;
using PanelForge.Application.Features.Auth.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Admin.Queries.GetAdminUserDetail;

/// <summary>GET /api/admin/users/{id} — trang chi tiết tài khoản cho Admin (OPS-30, UC-01, BR-18).</summary>
public sealed record GetAdminUserDetailQuery(Guid UserId, int ActivityLimit = 20) : IRequest<Result<AdminUserDetailDto>>;

public sealed class GetAdminUserDetailQueryHandler(IPanelForgeDbContext db)
    : IRequestHandler<GetAdminUserDetailQuery, Result<AdminUserDetailDto>>
{
    public async Task<Result<AdminUserDetailDto>> Handle(GetAdminUserDetailQuery q, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == q.UserId, ct);
        if (user is null)
            return Result<AdminUserDetailDto>.Failure("Không tìm thấy tài khoản.", ResultErrorCodes.NotFound);

        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // Cùng điều kiện "phiên còn hiệu lực" với màn Thiết bị đăng nhập của chính người dùng (GetActiveSessions).
        var policy = await SessionPolicy.ForUserAsync(db, user.Id, ct);
        var tokens = await db.RefreshTokens.AsNoTracking()
            .Where(t => t.UserId == user.Id && t.RevokedAt == null && t.ExpiresAt > now)
            .ToListAsync(ct);
        var sessions = tokens
            .Where(t => !policy.IsSessionExpired(t.SessionStartedAt, now) && !policy.IsIdle(t.LastActiveAt, now))
            .GroupBy(t => t.SessionId)
            .Select(g =>
            {
                var latest = g.OrderByDescending(t => t.LastActiveAt).First();
                return new AdminUserSessionDto(g.Key, latest.DeviceName, latest.IpAddress, latest.UserAgent,
                    g.Min(t => t.SessionStartedAt), latest.LastActiveAt, g.Max(t => t.ExpiresAt));
            })
            .OrderByDescending(s => s.LastActiveAt)
            .ToList();
        var lastActiveAt = await db.RefreshTokens.AsNoTracking()
            .Where(t => t.UserId == user.Id)
            .MaxAsync(t => (DateTime?)t.LastActiveAt, ct);

        var memberships = await db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.UserId == user.Id)
            .OrderByDescending(m => m.JoinedAt)
            .Select(m => new AdminUserMembershipDto(
                m.WorkspaceId, m.Workspace.Name, m.Role, m.Status, m.Workspace.OwnerId == user.Id, m.JoinedAt))
            .ToListAsync(ct);

        var wallet = await db.ReaderWallets.AsNoTracking()
            .Where(w => w.UserId == user.Id)
            .Select(w => (decimal?)w.BalanceVnd)
            .FirstOrDefaultAsync(ct);
        var topUps = db.PaymentTransactions.AsNoTracking()
            .Where(t => t.UserId == user.Id && t.Type == PaymentTransactionType.Topup);
        var topUpTotal = await topUps.Where(t => t.Status == PaymentTransactionStatus.Approved).SumAsync(t => (decimal?)t.AmountVnd, ct) ?? 0m;
        var pendingTopUps = await topUps.CountAsync(t => t.Status == PaymentTransactionStatus.Pending, ct);
        var unlocked = await db.ChapterEntitlements.AsNoTracking().CountAsync(e => e.UserId == user.Id, ct);
        var walletDto = wallet is null && topUpTotal == 0 && pendingTopUps == 0 && unlocked == 0
            ? null
            : new AdminUserWalletDto(wallet ?? 0m, topUpTotal, unlocked, pendingTopUps);

        var usage = db.AiUsageRecords.AsNoTracking().Where(r => r.UserId == user.Id && r.OccurredAtUtc >= monthStart);
        var aiUsage = new AdminUserAiUsageDto(
            Tokens: await usage.SumAsync(r => (long?)r.TotalTokens, ct) ?? 0,
            Calls: await usage.CountAsync(ct),
            FailedCalls: await usage.CountAsync(r => r.Status != AiUsageStatus.Succeeded, ct));

        var activity = await db.AuditLogs.AsNoTracking()
            .Where(a => a.UserId == user.Id)
            .OrderByDescending(a => a.TimestampUtc)
            .Take(Math.Clamp(q.ActivityLimit, 1, 100))
            .Select(AuditLogFiltering.ToDto)
            .ToListAsync(ct);

        return Result<AdminUserDetailDto>.Success(new AdminUserDetailDto(
            user.Id, user.Email, user.FullName, user.PhoneNumber, user.AvatarUrl, user.Role.ToString(),
            user.CanCreateStudio, user.IsActive, user.IsEmailConfirmed, user.IsPendingInvitation, user.TwoFactorEnabled,
            user.CreatedAt, user.UpdatedAt, lastActiveAt,
            sessions, memberships, walletDto,
            aiUsage,
            activity));
    }
}
