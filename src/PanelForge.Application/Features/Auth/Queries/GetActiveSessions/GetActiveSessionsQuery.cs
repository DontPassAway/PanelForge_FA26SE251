using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Auth.Common;

namespace PanelForge.Application.Features.Auth.Queries.GetActiveSessions;

public sealed record ActiveSessionDto(
    Guid SessionId,
    string DeviceName,
    string? IpAddress,
    DateTime SessionStartedAt,
    DateTime LastActiveAt,
    bool IsCurrent);

public sealed record ActiveSessionsResponse(
    int MaxConcurrentSessions,
    IReadOnlyList<ActiveSessionDto> Sessions);

public sealed record GetActiveSessionsQuery(Guid UserId, Guid? CurrentSessionId)
    : IRequest<ActiveSessionsResponse>;

public sealed class GetActiveSessionsQueryHandler
    : IRequestHandler<GetActiveSessionsQuery, ActiveSessionsResponse>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetActiveSessionsQueryHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ActiveSessionsResponse> Handle(GetActiveSessionsQuery request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var policy = await SessionPolicy.ForUserAsync(_dbContext, request.UserId, cancellationToken);

        var tokens = await _dbContext.RefreshTokens.AsNoTracking()
            .Where(t => t.UserId == request.UserId && t.RevokedAt == null && t.ExpiresAt > now)
            .ToListAsync(cancellationToken);

        var sessions = tokens
            .Where(t => !policy.IsSessionExpired(t.SessionStartedAt, now) && !policy.IsIdle(t.LastActiveAt, now))
            .GroupBy(t => t.SessionId)
            .Select(g =>
            {
                var latest = g.OrderByDescending(t => t.LastActiveAt).First();
                return new ActiveSessionDto(
                    SessionId: g.Key,
                    DeviceName: string.IsNullOrWhiteSpace(latest.DeviceName) ? "Thiết bị không xác định" : latest.DeviceName,
                    IpAddress: latest.IpAddress,
                    SessionStartedAt: g.Min(t => t.SessionStartedAt),
                    LastActiveAt: latest.LastActiveAt,
                    IsCurrent: request.CurrentSessionId.HasValue && g.Key == request.CurrentSessionId.Value);
            })
            .OrderByDescending(s => s.IsCurrent)
            .ThenByDescending(s => s.LastActiveAt)
            .ToList();

        return new ActiveSessionsResponse(
            MaxConcurrentSessions: policy.MaxSessions ?? SessionPolicy.DefaultReaderMaxSessions,
            Sessions: sessions);
    }
}
