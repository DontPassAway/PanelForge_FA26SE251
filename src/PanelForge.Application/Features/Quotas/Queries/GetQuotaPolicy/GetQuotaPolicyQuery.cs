using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Quotas.Common;
using PanelForge.Application.Features.Quotas.Models;
using PanelForge.Application.Features.Workspaces.Models;

namespace PanelForge.Application.Features.Quotas.Queries.GetQuotaPolicy;

/// <summary>GET /api/admin/quota-policy — chính sách hạn mức cho Studio mới (OPS-20).</summary>
public sealed record GetQuotaPolicyQuery : IRequest<Result<QuotaPolicyDto>>;

public sealed class GetQuotaPolicyQueryHandler(IPanelForgeDbContext db, WorkspaceDefaultsSettings defaults)
    : IRequestHandler<GetQuotaPolicyQuery, Result<QuotaPolicyDto>>
{
    public async Task<Result<QuotaPolicyDto>> Handle(GetQuotaPolicyQuery request, CancellationToken ct)
        => Result<QuotaPolicyDto>.Success(await QuotaPolicies.GetEffectiveAsync(db, defaults, ct));
}
