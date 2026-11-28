using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Resources.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Resources.Queries.GetResourceRequests;

/// <summary>
/// Danh sách yêu cầu tăng tài nguyên, mới nhất trước.
/// Admin: GET /api/admin/resource-requests (WorkspaceId null = mọi Studio). Producer: GET /api/workspaces/{id}/resource-requests.
/// </summary>
public sealed record GetResourceRequestsQuery(
    Guid? WorkspaceId = null,
    ResourceRequestStatus? Status = null,
    ResourceType? ResourceType = null,
    string? Search = null) : IRequest<Result<IReadOnlyList<ResourceRequestDto>>>;

public sealed class GetResourceRequestsQueryHandler : IRequestHandler<GetResourceRequestsQuery, Result<IReadOnlyList<ResourceRequestDto>>>
{
    // shortcut: không phân trang; lượng yêu cầu thực tế nhỏ (≤ vài trăm). Thêm page/pageSize khi vượt mức đó.
    private const int MaxRows = 500;

    private readonly IPanelForgeDbContext _db;
    public GetResourceRequestsQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<ResourceRequestDto>>> Handle(GetResourceRequestsQuery q, CancellationToken ct)
    {
        var query = _db.ResourceRequests.AsNoTracking().AsQueryable();
        if (q.WorkspaceId is { } wsId) query = query.Where(r => r.WorkspaceId == wsId);
        if (q.Status is { } status) query = query.Where(r => r.Status == status);
        if (q.ResourceType is { } type) query = query.Where(r => r.ResourceType == type);

        var rows = await (
            from r in query
            join w in _db.StudioWorkspaces.AsNoTracking() on r.WorkspaceId equals w.Id
            join u in _db.Users.AsNoTracking() on r.RequestedBy equals u.Id into requesters
            from u in requesters.DefaultIfEmpty()
            join a in _db.Users.AsNoTracking() on r.ReviewedBy equals a.Id into reviewers
            from a in reviewers.DefaultIfEmpty()
            orderby r.CreatedAt descending
            select new ResourceRequestDto(
                r.Id, r.WorkspaceId, w.Name, r.ResourceType, r.CurrentLimit, r.RequestedLimit, r.Reason,
                r.RequestedBy, u != null ? u.FullName : string.Empty, r.CreatedAt, r.Status,
                r.ReviewedBy, a != null ? a.FullName : null, r.ReviewedAt, r.ReviewNote))
            .Take(MaxRows)
            .ToListAsync(ct);

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            rows = rows.Where(r => r.WorkspaceName.Contains(s, StringComparison.OrdinalIgnoreCase)
                                   || r.RequestedByName.Contains(s, StringComparison.OrdinalIgnoreCase)
                                   || r.Reason.Contains(s, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        return Result<IReadOnlyList<ResourceRequestDto>>.Success(rows);
    }
}
