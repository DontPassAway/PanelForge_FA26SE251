using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Payments.Common;
using PanelForge.Application.Features.Payments.Models;

namespace PanelForge.Application.Features.Payments.Queries.GetStudioPackage;

/// <summary>GET /api/catalog/studio-package — giá gói mở Studio đang bán (D11), không cần đăng nhập.</summary>
public sealed record GetStudioPackageQuery : IRequest<Result<StudioPackageDto>>;

public sealed class GetStudioPackageQueryHandler : IRequestHandler<GetStudioPackageQuery, Result<StudioPackageDto>>
{
    private readonly IPanelForgeDbContext _db;

    public GetStudioPackageQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<StudioPackageDto>> Handle(GetStudioPackageQuery query, CancellationToken ct)
    {
        var price = await StudioPackage.CurrentPriceAsync(_db, ct);
        return Result<StudioPackageDto>.Success(new StudioPackageDto(price is not null, price?.PriceVnd));
    }
}
