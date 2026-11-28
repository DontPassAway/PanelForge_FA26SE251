using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Quotas.Models;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Quotas.Queries.GetQuotaIncreaseRequests;

/// <summary>
/// Danh sách yêu cầu tăng hạn mức (OPS-21), mới nhất trước.
/// WorkspaceId có giá trị: Producer xem của Studio mình (<c>GET /api/workspaces/{id}/quota-requests</c>).
/// WorkspaceId null: Admin xem toàn nền tảng (<c>GET /api/admin/quota-requests</c>).
/// </summary>
public sealed record GetQuotaIncreaseRequestsQuery(Guid? WorkspaceId, QuotaIncreaseRequestStatus? Status, int Limit = 200)
    : IRequest<Result<IReadOnlyList<QuotaIncreaseRequestDto>>>;

public sealed class GetQuotaIncreaseRequestsQueryHandler(IPanelForgeDbContext db)
    : IRequestHandler<GetQuotaIncreaseRequestsQuery, Result<IReadOnlyList<QuotaIncreaseRequestDto>>>
{
    public async Task<Result<IReadOnlyList<QuotaIncreaseRequestDto>>> Handle(GetQuotaIncreaseRequestsQuery q, CancellationToken ct)
    {
        var query = db.QuotaIncreaseRequests.AsNoTracking();
        if (q.WorkspaceId is { } wsId) query = query.Where(r => r.WorkspaceId == wsId);
        if (q.Status is { } status) query = query.Where(r => r.Status == status);

        var rows = await ToDtos(db, query.OrderByDescending(r => r.CreatedAt).Take(Math.Clamp(q.Limit, 1, 500)))
            .ToListAsync(ct);
        return Result<IReadOnlyList<QuotaIncreaseRequestDto>>.Success(rows);
    }

    internal static IQueryable<QuotaIncreaseRequestDto> ToDtos(IPanelForgeDbContext db, IQueryable<QuotaIncreaseRequest> query)
        => query.Select(r => new QuotaIncreaseRequestDto(
            r.Id,
            r.WorkspaceId,
            r.Workspace.Name,
            r.ResourceType,
            r.CurrentQuota,
            r.RequestedQuota,
            r.Reason,
            r.Status,
            r.RequestedByUserId,
            db.Users.Where(u => u.Id == r.RequestedByUserId).Select(u => u.FullName).FirstOrDefault() ?? string.Empty,
            r.CreatedAt,
            r.ReviewedByUserId,
            db.Users.Where(u => u.Id == r.ReviewedByUserId).Select(u => u.FullName).FirstOrDefault(),
            r.ReviewedAt,
            r.ReviewNote,
            r.GrantedQuota));
}
