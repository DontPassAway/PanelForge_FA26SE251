using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Resources.Common;
using PanelForge.Application.Features.Resources.Models;
using PanelForge.Domain.Entities.Workspaces;

namespace PanelForge.Application.Features.Resources.Queries.GetResourcePolicy;

/// <summary>GET /api/admin/resource-policy — chính sách tài nguyên mặc định cho Studio mới.</summary>
public sealed record GetResourcePolicyQuery : IRequest<Result<ResourcePolicyDto>>;

public sealed class GetResourcePolicyQueryHandler : IRequestHandler<GetResourcePolicyQuery, Result<ResourcePolicyDto>>
{
    private readonly IPanelForgeDbContext _db;
    public GetResourcePolicyQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ResourcePolicyDto>> Handle(GetResourcePolicyQuery q, CancellationToken ct)
    {
        var saved = await ResourceAllocation.FindSavedPolicyAsync(_db, ct);
        return Result<ResourcePolicyDto>.Success(ResourceAllocation.ToDto(saved ?? ResourcePolicy.CreateDefault(), saved is not null));
    }
}
