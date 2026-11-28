using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Resources.Models;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Resources.Common;

/// <summary>Đọc chính sách mặc định và áp giới hạn tài nguyên lên Studio — dùng chung cho tạo Studio, Admin chỉnh tay và duyệt yêu cầu.</summary>
public static class ResourceAllocation
{
    /// <summary>Chính sách đã lưu (tracked), null nếu Admin chưa lưu lần nào.</summary>
    public static Task<ResourcePolicy?> FindSavedPolicyAsync(IPanelForgeDbContext db, CancellationToken ct)
        => db.ResourcePolicies.OrderBy(p => p.CreatedAt).FirstOrDefaultAsync(ct);

    /// <summary>Chính sách đã lưu hoặc giá trị mặc định phát triển (không ghi DB).</summary>
    public static async Task<ResourcePolicy> GetPolicyAsync(IPanelForgeDbContext db, CancellationToken ct)
        => await FindSavedPolicyAsync(db, ct) ?? ResourcePolicy.CreateDefault();

    /// <summary>Cấu hình AI ban đầu cho Studio mới theo chính sách.</summary>
    public static WorkspaceAiConfig NewAiConfig(Guid workspaceId, ResourcePolicy policy)
    {
        var config = WorkspaceAiConfig.Create(workspaceId, monthlyTokenQuota: policy.DefaultMonthlyTokenQuota);
        config.SetMonthlyImageQuota(policy.DefaultMonthlyImageQuota);
        if (!policy.DefaultAiEnabled)
            config.UpdateSettings(config.Provider, null, false, policy.DefaultMonthlyTokenQuota, string.Empty);
        return config;
    }

    /// <summary>Giới hạn hiện tại của một loại tài nguyên (0 = không giới hạn).</summary>
    public static long CurrentLimit(ResourceType type, StudioWorkspace workspace, WorkspaceAiConfig? ai) => type switch
    {
        ResourceType.Storage => workspace.StorageQuotaBytes,
        ResourceType.AiTokens => ai?.MonthlyTokenQuota ?? 0,
        ResourceType.AiImages => ai?.MonthlyImageQuota ?? 0,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    /// <summary>Đặt giới hạn mới cho một loại tài nguyên. Hạ quota dưới mức đang dùng KHÔNG xoá file nào.</summary>
    public static void Apply(ResourceType type, long limit, StudioWorkspace workspace, WorkspaceAiConfig ai)
    {
        switch (type)
        {
            case ResourceType.Storage: workspace.SetStorageQuota(limit); break;
            case ResourceType.AiTokens: ai.UpdateSettings(ai.Provider, null, ai.IsEnabled, limit, string.Empty); break;
            case ResourceType.AiImages: ai.SetMonthlyImageQuota(limit); break;
            default: throw new ArgumentOutOfRangeException(nameof(type));
        }
    }

    public static ResourceHealth Health(int warningPercent, params double?[] percents)
    {
        var worst = percents.Where(p => p.HasValue).Select(p => p!.Value).DefaultIfEmpty(0).Max();
        return worst >= 100 ? ResourceHealth.Exhausted : worst >= warningPercent ? ResourceHealth.NearLimit : ResourceHealth.Ok;
    }

    /// <summary>AiUsageService reset quota khi sang tháng UTC mới → kỳ reset kế tiếp là ngày 1 tháng sau (UTC).</summary>
    public static DateTime NextMonthlyResetUtc(DateTime nowUtc)
        => new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);

    public static ResourcePolicyDto ToDto(ResourcePolicy p, bool isConfigured) => new(
        p.DefaultStorageQuotaBytes, p.DefaultMonthlyTokenQuota, p.DefaultMonthlyImageQuota,
        p.DefaultAiEnabled, p.WarningThresholdPercent, isConfigured, p.UpdatedAt);
}
