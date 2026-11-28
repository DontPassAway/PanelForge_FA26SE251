using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.StudioResources.Models;
using PanelForge.Domain.Entities.Payments;

namespace PanelForge.Application.Features.StudioResources.Queries.GetAdminStudioResourcePrice;

/// <summary>GET /api/admin/studio-resources/price — đơn giá tài nguyên Studio đang bán (D12).</summary>
public sealed record GetAdminStudioResourcePriceQuery : IRequest<Result<AdminStudioResourcePriceDto>>;

public sealed class GetAdminStudioResourcePriceQueryHandler
    : IRequestHandler<GetAdminStudioResourcePriceQuery, Result<AdminStudioResourcePriceDto>>
{
    private readonly IPanelForgeDbContext _db;

    public GetAdminStudioResourcePriceQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<AdminStudioResourcePriceDto>> Handle(GetAdminStudioResourcePriceQuery query, CancellationToken ct)
        => Result<AdminStudioResourcePriceDto>.Success(await LoadAsync(_db, ct));

    public static async Task<AdminStudioResourcePriceDto> LoadAsync(IPanelForgeDbContext db, CancellationToken ct)
    {
        var latest = await db.StudioResourcePrices.AsNoTracking()
            .Where(p => !p.IsDeleted)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                p.PricePerGbVnd,
                p.PricePerMillionTokensVnd,
                p.CreatedAt,
                ChangedByName = db.Users.Where(u => u.Id == p.ChangedByUserId).Select(u => u.FullName).FirstOrDefault()
            })
            .FirstOrDefaultAsync(ct);

        return new AdminStudioResourcePriceDto(
            latest?.PricePerGbVnd, latest?.PricePerMillionTokensVnd, latest?.CreatedAt, latest?.ChangedByName,
            StudioResourcePrice.MinPriceVnd, StudioResourcePrice.MaxPriceVnd);
    }
}
