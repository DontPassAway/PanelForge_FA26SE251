using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Payments;

namespace PanelForge.Application.Features.Payments.Common;

/// <summary>Giá gói mở Studio hiện hành (D11): bản ghi mới nhất của <c>studio_package_prices</c>; null = chưa bán.</summary>
public static class StudioPackage
{
    public static Task<StudioPackagePrice?> CurrentPriceAsync(IPanelForgeDbContext db, CancellationToken ct) =>
        db.StudioPackagePrices
            .AsNoTracking()
            .Where(p => !p.IsDeleted)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);
}
