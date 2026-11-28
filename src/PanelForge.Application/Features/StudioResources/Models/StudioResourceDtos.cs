namespace PanelForge.Application.Features.StudioResources.Models;

/// <summary>Một loại tài nguyên trong kho Producer: tổng đã mua, đã chia cho các Studio, còn lại để chia tiếp.</summary>
public sealed record ResourceBudgetDto(long Total, long Allocated, long Remaining);

/// <summary>Phần một Studio được chia và đang dùng. Quota 0 = không giới hạn (Studio tạo trước khi có kho).</summary>
public sealed record StudioAllocationDto(
    Guid WorkspaceId,
    string Name,
    long StorageQuotaBytes,
    long UsedStorageBytes,
    long TokenQuota,
    long UsedTokens);

/// <summary>
/// GET /api/workspaces/resources — kho tài nguyên của Producer đang đăng nhập (D12).
/// <paramref name="HasPool"/> = false: chưa có kho (cấp quyền trước D12), Storage/Tokens null và Studio mới
/// nhận hạn mức mặc định như trước. <paramref name="SuggestedStorageBytes"/>/<paramref name="SuggestedTokens"/>:
/// hạn mức mặc định của chính sách Admin, để gợi ý khi tạo Studio mới. Đơn giá null = Admin chưa mở bán tài nguyên.
/// </summary>
public sealed record StudioResourcesDto(
    bool HasPool,
    ResourceBudgetDto? Storage,
    ResourceBudgetDto? Tokens,
    IReadOnlyList<StudioAllocationDto> Studios,
    long SuggestedStorageBytes,
    long SuggestedTokens,
    int? PricePerGbVnd = null,
    int? PricePerMillionTokensVnd = null);

public sealed record AllocateStudioResourcesRequest(long StorageQuotaBytes, long TokenQuota);

/// <summary>Producer mua thêm bằng Ví: số GB và số triệu token; <c>ExpectedTotalVnd</c> lệch giá hiện hành → 409.</summary>
public sealed record PurchaseStudioResourcesRequest(int StorageGb, int TokenMillions, int ExpectedTotalVnd);

public sealed record PurchaseStudioResourcesResponse(
    int TotalVnd,
    int RemainingBalanceVnd,
    string OrderCode,
    StudioResourcesDto Resources);

/// <summary>Đơn giá tài nguyên cho màn Admin (null = chưa bán).</summary>
public sealed record AdminStudioResourcePriceDto(
    int? PricePerGbVnd,
    int? PricePerMillionTokensVnd,
    DateTime? ChangedAt,
    string? ChangedByName,
    int MinPriceVnd,
    int MaxPriceVnd);

public sealed record SetStudioResourcePriceRequest(int PricePerGbVnd, int PricePerMillionTokensVnd);

/// <summary>Admin bán thêm tài nguyên: cộng vào kho của Producer.</summary>
public sealed record GrantStudioResourcesRequest(long StorageBytes, long Tokens);
