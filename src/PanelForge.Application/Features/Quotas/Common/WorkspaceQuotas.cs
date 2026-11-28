using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Quotas.Common;

/// <summary>Đọc/ghi một loại hạn mức của Studio: dung lượng nằm ở Studio, token và ảnh AI nằm ở cấu hình AI.</summary>
internal static class WorkspaceQuotas
{
    /// <summary>Mức đang cấp; null nếu Studio chưa có cấu hình AI (loại AI). 0 = không giới hạn.</summary>
    public static long? Current(QuotaResourceType type, StudioWorkspace workspace, WorkspaceAiConfig? ai) => type switch
    {
        QuotaResourceType.Storage => workspace.StorageQuotaBytes,
        QuotaResourceType.AiTokens => ai?.MonthlyTokenQuota,
        QuotaResourceType.AiImages => ai?.MonthlyImageQuota,
        QuotaResourceType.AiRequests => ai?.MonthlyRequestQuota,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    /// <summary>Đặt mức mới; tạo cấu hình AI mặc định nếu Studio chưa có. Trả cấu hình AI (có thể mới tạo).</summary>
    public static WorkspaceAiConfig? Apply(
        IPanelForgeDbContext db, QuotaResourceType type, long quota, StudioWorkspace workspace, WorkspaceAiConfig? ai)
    {
        if (type == QuotaResourceType.Storage)
        {
            workspace.SetStorageQuota(quota);
            return ai;
        }

        if (ai is null)
        {
            ai = WorkspaceAiConfig.Create(workspace.Id);
            db.WorkspaceAiConfigs.Add(ai);
        }

        switch (type)
        {
            case QuotaResourceType.AiTokens: ai.SetMonthlyTokenQuota(quota); break;
            case QuotaResourceType.AiImages: ai.SetMonthlyImageQuota(quota); break;
            case QuotaResourceType.AiRequests: ai.SetRequestLimits(quota, null); break;
            default: throw new ArgumentOutOfRangeException(nameof(type));
        }
        return ai;
    }

    public static Task<WorkspaceAiConfig?> LoadAiConfigAsync(IPanelForgeDbContext db, Guid workspaceId, CancellationToken ct)
        => db.WorkspaceAiConfigs.FirstOrDefaultAsync(c => c.WorkspaceId == workspaceId, ct);
}
