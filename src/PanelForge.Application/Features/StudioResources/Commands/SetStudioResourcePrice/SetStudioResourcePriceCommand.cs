using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.StudioResources.Models;
using PanelForge.Application.Features.StudioResources.Queries.GetAdminStudioResourcePrice;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Domain.Entities.Payments;

namespace PanelForge.Application.Features.StudioResources.Commands.SetStudioResourcePrice;

/// <summary>
/// PUT /api/admin/studio-resources/price — Admin đặt đơn giá mỗi GB và mỗi 1 triệu token (D12). Append-only kèm audit;
/// Producer đang mua theo giá cũ nhận 409 PRICE_CHANGED.
/// </summary>
public sealed record SetStudioResourcePriceCommand(Guid AdminUserId, int PricePerGbVnd, int PricePerMillionTokensVnd)
    : IRequest<Result<AdminStudioResourcePriceDto>>;

public sealed class SetStudioResourcePriceCommandHandler
    : IRequestHandler<SetStudioResourcePriceCommand, Result<AdminStudioResourcePriceDto>>
{
    private readonly IPanelForgeDbContext _db;

    public SetStudioResourcePriceCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<AdminStudioResourcePriceDto>> Handle(SetStudioResourcePriceCommand command, CancellationToken ct)
    {
        StudioResourcePrice price;
        try
        {
            price = StudioResourcePrice.Create(command.PricePerGbVnd, command.PricePerMillionTokensVnd, command.AdminUserId);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Result<AdminStudioResourcePriceDto>.Failure(
                $"Đơn giá tài nguyên phải từ {StudioResourcePrice.MinPriceVnd:N0}đ đến {StudioResourcePrice.MaxPriceVnd:N0}đ.",
                ResultErrorCodes.Validation);
        }

        _db.StudioResourcePrices.Add(price);
        _db.AuditLogs.Add(AuditLog.Create(
            action: "STUDIO_RESOURCE_PRICE_CHANGED",
            entityName: nameof(StudioResourcePrice),
            entityId: price.Id.ToString(),
            userId: command.AdminUserId,
            details: $"Đơn giá tài nguyên Studio: {price.PricePerGbVnd:N0}đ/GB, {price.PricePerMillionTokensVnd:N0}đ/1 triệu token"));
        await _db.SaveChangesAsync(ct);

        return Result<AdminStudioResourcePriceDto>.Success(await GetAdminStudioResourcePriceQueryHandler.LoadAsync(_db, ct));
    }
}
