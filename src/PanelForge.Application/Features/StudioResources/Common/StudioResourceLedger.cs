using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Quotas.Common;
using PanelForge.Application.Features.StudioResources.Models;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Domain.Entities.Payments;
using PanelForge.Domain.Entities.Workspaces;

namespace PanelForge.Application.Features.StudioResources.Common;

/// <summary>Tính phần đã chia / còn lại của kho Producer từ hạn mức hiện tại của các Studio họ sở hữu (D12).</summary>
public static class StudioResourceLedger
{
    public static async Task<IReadOnlyList<StudioAllocationDto>> OwnedStudiosAsync(
        IPanelForgeDbContext db, Guid ownerId, CancellationToken ct)
    {
        var studios = await db.StudioWorkspaces.AsNoTracking()
            .Where(w => w.OwnerId == ownerId && !w.IsDeleted)
            .OrderBy(w => w.CreatedAt)
            .Select(w => new
            {
                w.Id,
                w.Name,
                w.StorageQuotaBytes,
                w.UsedStorageBytes,
                Ai = db.WorkspaceAiConfigs
                    .Where(c => c.WorkspaceId == w.Id)
                    .Select(c => new { c.MonthlyTokenQuota, c.UsedTokensCurrentMonth })
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        return studios
            .Select(s => new StudioAllocationDto(
                s.Id, s.Name, s.StorageQuotaBytes, s.UsedStorageBytes,
                s.Ai?.MonthlyTokenQuota ?? 0, s.Ai?.UsedTokensCurrentMonth ?? 0))
            .ToList();
    }

    public static async Task<StudioResourcesDto> LoadAsync(
        IPanelForgeDbContext db, WorkspaceDefaultsSettings defaults, Guid ownerId, CancellationToken ct)
    {
        var pool = await db.StudioResourcePools.AsNoTracking()
            .FirstOrDefaultAsync(p => p.OwnerId == ownerId && !p.IsDeleted, ct);
        var studios = await OwnedStudiosAsync(db, ownerId, ct);
        var policy = await QuotaPolicies.GetEffectiveAsync(db, defaults, ct);
        var price = await CurrentPriceAsync(db, ct);

        if (pool is null)
            return new StudioResourcesDto(false, null, null, studios, policy.StorageQuotaBytes, policy.MonthlyTokenQuota,
                price?.PricePerGbVnd, price?.PricePerMillionTokensVnd);

        var storageAllocated = studios.Sum(s => s.StorageQuotaBytes);
        var tokensAllocated = studios.Sum(s => s.TokenQuota);
        return new StudioResourcesDto(
            true,
            Budget(pool.StorageBytes, storageAllocated),
            Budget(pool.Tokens, tokensAllocated),
            studios,
            policy.StorageQuotaBytes,
            policy.MonthlyTokenQuota,
            price?.PricePerGbVnd,
            price?.PricePerMillionTokensVnd);
    }

    public static Task<StudioResourcePrice?> CurrentPriceAsync(IPanelForgeDbContext db, CancellationToken ct) =>
        db.StudioResourcePrices.AsNoTracking()
            .Where(p => !p.IsDeleted)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Lấy kho (đang theo dõi để ghi) của Producer; chưa có thì tạo từ đúng phần các Studio hiện có đang giữ,
    /// để phần cộng thêm sau đó là phần còn lại để chia. Studio cũ không giới hạn (0) không chiếm kho.
    /// </summary>
    public static async Task<StudioResourcePool> EnsurePoolAsync(IPanelForgeDbContext db, Guid ownerId, CancellationToken ct)
    {
        var pool = await db.StudioResourcePools.FirstOrDefaultAsync(p => p.OwnerId == ownerId && !p.IsDeleted, ct);
        if (pool is not null) return pool;

        var studios = await OwnedStudiosAsync(db, ownerId, ct);
        pool = StudioResourcePool.Create(ownerId, studios.Sum(s => s.StorageQuotaBytes), studios.Sum(s => s.TokenQuota));
        db.StudioResourcePools.Add(pool);
        return pool;
    }

    public const long BytesPerGb = 1L << 30;
    public const long TokensPerUnit = 1_000_000;

    private static ResourceBudgetDto Budget(long total, long allocated)
        => new(total, allocated, Math.Max(0, total - allocated));

    public static string FormatBytes(long bytes)
        => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.##} GB" : $"{bytes / (double)(1L << 20):0.##} MB";
}
