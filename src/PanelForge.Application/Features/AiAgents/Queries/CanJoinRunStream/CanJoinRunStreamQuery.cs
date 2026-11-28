using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;

namespace PanelForge.Application.Features.AiAgents.Queries.CanJoinRunStream;

/// <summary>
/// Người dùng có được nghe sự kiện realtime (SignalR) của một AI run không: cùng luật với <c>GetRunQuery</c>,
/// tức phải là thành viên Series của run (BR-07, BR-23). Run không tồn tại → false (không lộ run có tồn tại hay không).
/// </summary>
public sealed record CanJoinRunStreamQuery(Guid RunId, Guid UserId) : IRequest<bool>;

public sealed class CanJoinRunStreamQueryHandler : IRequestHandler<CanJoinRunStreamQuery, bool>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;

    public CanJoinRunStreamQueryHandler(IPanelForgeDbContext db, IWorkspaceAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<bool> Handle(CanJoinRunStreamQuery q, CancellationToken ct)
    {
        if (q.UserId == Guid.Empty) return false;

        var seriesId = await _db.AiWorkflowRuns.AsNoTracking()
            .Where(r => r.Id == q.RunId)
            .Select(r => (Guid?)r.SeriesId)
            .FirstOrDefaultAsync(ct);
        if (seriesId is null) return false;

        return await _auth.GetSeriesMemberRoleAsync(q.UserId, seriesId.Value, ct) is not null;
    }
}
