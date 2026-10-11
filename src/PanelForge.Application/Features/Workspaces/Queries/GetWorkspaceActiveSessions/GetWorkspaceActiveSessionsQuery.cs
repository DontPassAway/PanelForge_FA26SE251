using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Workspaces.Queries.GetWorkspaceActiveSessions;

public sealed record WorkspaceSessionDto(
    Guid SessionId,
    Guid UserId,
    string FullName,
    string Email,
    string Role,
    string? DeviceName,
    string? IpAddress,
    string? UserAgent,
    DateTime SessionStartedAt,
    DateTime LastActiveAt,
    bool IsCurrentSession);

/// <param name="CurrentSessionId">Phiên của chính request này (claim <c>session_id</c> trong JWT); null nếu token không mang claim.</param>
public sealed record GetWorkspaceActiveSessionsQuery(Guid WorkspaceId, Guid? CurrentSessionId = null)
    : IRequest<Result<IReadOnlyList<WorkspaceSessionDto>>>;

public sealed class GetWorkspaceActiveSessionsQueryHandler
    : IRequestHandler<GetWorkspaceActiveSessionsQuery, Result<IReadOnlyList<WorkspaceSessionDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public GetWorkspaceActiveSessionsQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<WorkspaceSessionDto>>> Handle(
        GetWorkspaceActiveSessionsQuery q, CancellationToken ct)
    {
        var workspace = await _db.StudioWorkspaces.AsNoTracking()
            .Where(w => w.Id == q.WorkspaceId)
            .Select(w => new { w.SessionIdleTimeoutMinutes })
            .FirstOrDefaultAsync(ct);
        if (workspace is null)
            return Result<IReadOnlyList<WorkspaceSessionDto>>.Failure("Workspace không tồn tại.", ResultErrorCodes.NotFound);

        var members = await _db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.WorkspaceId == q.WorkspaceId)
            .Include(m => m.User)
            .ToListAsync(ct);

        var memberMap = members.ToDictionary(m => m.UserId);
        var memberUserIds = members.Select(m => m.UserId).ToList();

        var now = DateTime.UtcNow;
        // Phiên đã quá idle timeout không còn dùng được (AccessTokenGuard từ chối) nên không liệt kê; cùng giới hạn với GetWorkspaceUsage.
        DateTime? idleCutoff = workspace.SessionIdleTimeoutMinutes is { } idle ? now.AddMinutes(-idle) : null;
        var activeTokens = await _db.RefreshTokens.AsNoTracking()
            .Where(t => memberUserIds.Contains(t.UserId) && t.RevokedAt == null && t.ExpiresAt > now
                        && (idleCutoff == null || t.LastActiveAt > idleCutoff))
            .OrderByDescending(t => t.LastActiveAt)
            .ToListAsync(ct);

        var sessions = activeTokens
            .GroupBy(t => t.SessionId)
            .Select(g =>
            {
                var latestToken = g.OrderByDescending(t => t.CreatedAt).First();
                memberMap.TryGetValue(latestToken.UserId, out var member);
                var user = member?.User;

                return new WorkspaceSessionDto(
                    SessionId: g.Key,
                    UserId: latestToken.UserId,
                    FullName: user?.FullName ?? user?.Email.Split('@')[0] ?? "Thành viên",
                    Email: user?.Email ?? string.Empty,
                    Role: member?.Role.ToString() ?? "Member",
                    DeviceName: latestToken.DeviceName ?? "Thiết bị không xác định",
                    IpAddress: latestToken.IpAddress,
                    UserAgent: latestToken.UserAgent,
                    SessionStartedAt: latestToken.SessionStartedAt,
                    LastActiveAt: latestToken.LastActiveAt,
                    IsCurrentSession: g.Key == q.CurrentSessionId
                );
            })
            .OrderByDescending(s => s.LastActiveAt)
            .ToList();

        return Result<IReadOnlyList<WorkspaceSessionDto>>.Success(sessions);
    }
}
