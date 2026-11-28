using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Admin.Models;
using PanelForge.Domain.Entities.Payments;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Admin.Queries.GetAdminStudioPackage;

/// <summary>GET /api/admin/studio-package — giá gói mở Studio đang bán và số lượt mua (D11).</summary>
public sealed record GetAdminStudioPackageQuery : IRequest<Result<AdminStudioPackageDto>>;

public sealed class GetAdminStudioPackageQueryHandler : IRequestHandler<GetAdminStudioPackageQuery, Result<AdminStudioPackageDto>>
{
    private readonly IPanelForgeDbContext _db;

    public GetAdminStudioPackageQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<AdminStudioPackageDto>> Handle(GetAdminStudioPackageQuery query, CancellationToken ct)
        => Result<AdminStudioPackageDto>.Success(await LoadAsync(_db, ct));

    internal static async Task<AdminStudioPackageDto> LoadAsync(IPanelForgeDbContext db, CancellationToken ct)
    {
        var current = await (
                from p in db.StudioPackagePrices.AsNoTracking()
                join u in db.Users.AsNoTracking() on p.ChangedByUserId equals u.Id into changedBy
                from u in changedBy.DefaultIfEmpty()
                where !p.IsDeleted
                orderby p.CreatedAt descending
                select new { p.PriceVnd, p.CreatedAt, ChangedByName = u != null ? u.FullName : null })
            .FirstOrDefaultAsync(ct);

        var purchaseCount = await db.PaymentTransactions.AsNoTracking()
            .CountAsync(t => !t.IsDeleted
                             && t.Type == PaymentTransactionType.StudioPackage
                             && t.Status == PaymentTransactionStatus.Approved, ct);

        return new AdminStudioPackageDto(
            current?.PriceVnd, current?.CreatedAt, current?.ChangedByName, purchaseCount,
            StudioPackagePrice.MinPriceVnd, StudioPackagePrice.MaxPriceVnd);
    }
}
