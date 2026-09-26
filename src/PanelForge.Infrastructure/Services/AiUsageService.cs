using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Interfaces;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Auth;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Services;

public sealed class AiUsageService : IAiUsageService
{
    private const string UnconfiguredProvider = "Unconfigured";

    private readonly IPanelForgeDbContext _dbContext;
    private readonly ICurrentUserService _currentUser;

    public AiUsageService(IPanelForgeDbContext dbContext, ICurrentUserService currentUser)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
    }

    public async Task<AiAvailability> CheckAvailabilityAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var config = await _dbContext.WorkspaceAiConfigs
            .FirstOrDefaultAsync(c => c.WorkspaceId == workspaceId, cancellationToken);

        if (config is null || string.IsNullOrWhiteSpace(config.ApiKeyEncrypted))
            return AiAvailability.Blocked(AiUsageStatus.Disabled, "Workspace chưa được Administrator cấu hình AI Provider.");

        if (!config.IsEnabled)
            return AiAvailability.Blocked(AiUsageStatus.Disabled, "Tính năng AI đang bị tắt cho Workspace này.");

        if (ResetIfNewMonth(config))
            await _dbContext.SaveChangesAsync(cancellationToken);

        if (!config.HasQuotaRemaining())
            return AiAvailability.Blocked(AiUsageStatus.QuotaExceeded,
                "Workspace đã dùng hết hạn mức token AI của tháng. Vui lòng tiếp tục bằng thao tác thủ công.");

        return AiAvailability.Available();
    }

    public async Task<AiUsageRecord> RecordAsync(AiUsageEntry entry, CancellationToken cancellationToken = default)
    {
        var config = await _dbContext.WorkspaceAiConfigs
            .FirstOrDefaultAsync(c => c.WorkspaceId == entry.WorkspaceId, cancellationToken);

        var record = AiUsageRecord.Create(
            workspaceId: entry.WorkspaceId,
            feature: entry.Feature,
            provider: config?.Provider ?? UnconfiguredProvider,
            status: entry.Status,
            userId: _currentUser.UserId,
            model: entry.Model,
            promptTokens: entry.PromptTokens,
            completionTokens: entry.CompletionTokens,
            costUsd: entry.CostUsd,
            errorMessage: entry.ErrorMessage,
            durationMs: entry.DurationMs);

        _dbContext.AiUsageRecords.Add(record);

        if (config is not null)
        {
            ResetIfNewMonth(config);
            if (entry.Status == AiUsageStatus.Succeeded)
                config.RecordUsage(record.TotalTokens);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return record;
    }

    /// <summary>Hạn mức là theo tháng (UC-02): sang tháng mới (UTC) thì đưa UsedTokensCurrentMonth về 0.</summary>
    private static bool ResetIfNewMonth(WorkspaceAiConfig config)
    {
        var now = DateTime.UtcNow;
        var last = config.LastResetAt ?? config.CreatedAt;
        if (last.Year == now.Year && last.Month == now.Month) return false;

        config.ResetMonthlyUsage();
        return true;
    }
}
