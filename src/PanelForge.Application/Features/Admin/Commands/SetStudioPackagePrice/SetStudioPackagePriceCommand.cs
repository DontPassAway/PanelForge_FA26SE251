using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Admin.Models;
using PanelForge.Application.Features.Admin.Queries.GetAdminStudioPackage;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Domain.Entities.Payments;

namespace PanelForge.Application.Features.Admin.Commands.SetStudioPackagePrice;

/// <summary>
/// PUT /api/admin/studio-package/price — Admin đặt giá gói mở Studio (D11). Append-only: thêm bản ghi giá mới và
/// ghi audit; người đang mua với giá cũ nhận 409 PRICE_CHANGED.
/// </summary>
public sealed record SetStudioPackagePriceCommand(Guid AdminUserId, int PriceVnd) : IRequest<Result<AdminStudioPackageDto>>;

public sealed class SetStudioPackagePriceCommandHandler : IRequestHandler<SetStudioPackagePriceCommand, Result<AdminStudioPackageDto>>
{
    private readonly IPanelForgeDbContext _db;

    public SetStudioPackagePriceCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<AdminStudioPackageDto>> Handle(SetStudioPackagePriceCommand command, CancellationToken ct)
    {
        if (command.PriceVnd is < StudioPackagePrice.MinPriceVnd or > StudioPackagePrice.MaxPriceVnd)
            return Result<AdminStudioPackageDto>.Failure(
                $"Giá gói mở Studio phải từ {StudioPackagePrice.MinPriceVnd:N0}đ đến {StudioPackagePrice.MaxPriceVnd:N0}đ.",
                ResultErrorCodes.Validation);

        var previous = await _db.StudioPackagePrices.AsNoTracking()
            .Where(p => !p.IsDeleted)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => (int?)p.PriceVnd)
            .FirstOrDefaultAsync(ct);

        var price = StudioPackagePrice.Create(command.PriceVnd, command.AdminUserId);
        _db.StudioPackagePrices.Add(price);
        _db.AuditLogs.Add(AuditLog.Create(
            action: "STUDIO_PACKAGE_PRICE_CHANGED",
            entityName: nameof(StudioPackagePrice),
            entityId: price.Id.ToString(),
            userId: command.AdminUserId,
            details: $"Đổi giá gói mở Studio: {(previous is null ? "chưa bán" : $"{previous:N0}đ")} → {price.PriceVnd:N0}đ"));
        await _db.SaveChangesAsync(ct);

        return Result<AdminStudioPackageDto>.Success(await GetAdminStudioPackageQueryHandler.LoadAsync(_db, ct));
    }
}
