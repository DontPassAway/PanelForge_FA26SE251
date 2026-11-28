namespace PanelForge.Application.Features.Admin.Models;

/// <summary>Gói mở Studio (D11) cho màn Admin: giá hiện hành (null = chưa bán) và số lượt đã mua.</summary>
public sealed record AdminStudioPackageDto(
    int? PriceVnd,
    DateTime? ChangedAt,
    string? ChangedByName,
    int PurchaseCount,
    int MinPriceVnd,
    int MaxPriceVnd);

public sealed record SetStudioPackagePriceRequest(int PriceVnd);
