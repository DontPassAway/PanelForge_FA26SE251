using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.StudioResources.Common;
using PanelForge.Application.Features.StudioResources.Models;
using PanelForge.Application.Features.Workspaces.Models;

namespace PanelForge.Application.Features.StudioResources.Queries.GetMyStudioResources;

/// <summary>GET /api/workspaces/resources — kho tài nguyên và phần chia cho từng Studio của người đang đăng nhập (D12).</summary>
public sealed record GetMyStudioResourcesQuery(Guid UserId) : IRequest<Result<StudioResourcesDto>>;

public sealed class GetMyStudioResourcesQueryHandler : IRequestHandler<GetMyStudioResourcesQuery, Result<StudioResourcesDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly WorkspaceDefaultsSettings _defaults;

    public GetMyStudioResourcesQueryHandler(IPanelForgeDbContext db, WorkspaceDefaultsSettings defaults)
    {
        _db = db;
        _defaults = defaults;
    }

    public async Task<Result<StudioResourcesDto>> Handle(GetMyStudioResourcesQuery query, CancellationToken ct)
        => Result<StudioResourcesDto>.Success(await StudioResourceLedger.LoadAsync(_db, _defaults, query.UserId, ct));
}
