using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Entities.Ai;
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

        // TASK-355: Studio chưa tự chọn API vẫn dùng được nếu Admin đã đặt API mặc định (đang bật)
        if (config is null || (config.ProviderCredentialId is null && config.ImageProviderCredentialId is null
            && !await _dbContext.AiProviderCredentials.AnyAsync(c => c.IsEnabled && (c.IsDefaultText || c.IsDefaultImage), cancellationToken)))
            return AiAvailability.Blocked(AiUsageStatus.Disabled, "Studio chưa chọn API AI. Administrator cần chọn API trong Cấu hình AI.");

        if (!config.IsEnabled)
            return AiAvailability.Blocked(AiUsageStatus.Disabled, "Tính năng AI đang bị tắt cho Workspace này.");

        if (ResetIfNewMonth(config))
            await _dbContext.SaveChangesAsync(cancellationToken);

        if (!config.HasQuotaRemaining())
            return AiAvailability.Blocked(AiUsageStatus.QuotaExceeded,
                "Workspace đã dùng hết hạn mức token AI của tháng. Vui lòng tiếp tục bằng thao tác thủ công.");

        return AiAvailability.Available();
    }

    public async Task<bool> TryReserveTokensAsync(Guid workspaceId, long tokens, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            var config = await _dbContext.WorkspaceAiConfigs
                .FirstOrDefaultAsync(c => c.WorkspaceId == workspaceId, cancellationToken);
            if (config is null) return false;

            ResetIfNewMonth(config);
            if (!config.TryReserveTokens(tokens)) return false;
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < MaxConcurrencyRetries)
            {
                await ReloadAsync(ex);
            }
        }
    }

    public async Task<AiUsageRecord> RecordAsync(AiUsageEntry entry, CancellationToken cancellationToken = default)
    {
        var config = await _dbContext.WorkspaceAiConfigs
            .FirstOrDefaultAsync(c => c.WorkspaceId == entry.WorkspaceId, cancellationToken);

        var record = AiUsageRecord.Create(
            workspaceId: entry.WorkspaceId,
            feature: entry.Feature,
            provider: !string.IsNullOrWhiteSpace(entry.Provider) ? entry.Provider.Trim() : (config?.Provider ?? UnconfiguredProvider),
            status: entry.Status,
            userId: entry.UserId ?? _currentUser.UserId,
            model: entry.Model,
            promptTokens: entry.PromptTokens,
            completionTokens: entry.CompletionTokens,
            costUsd: entry.CostUsd,
            errorMessage: entry.ErrorMessage,
            durationMs: entry.DurationMs);

        _dbContext.AiUsageRecords.Add(record);

        // OPS-03: hai lượt AI xong cùng lúc không làm mất token của nhau (xmin): đụng nhau thì đọc lại rồi cộng lại
        for (var attempt = 0; ; attempt++)
        {
            if (config is not null)
            {
                ResetIfNewMonth(config);
                var used = entry.Status == AiUsageStatus.Succeeded ? record.TotalTokens : 0;
                if (entry.ReservedTokens > 0)
                    config.SettleReservedTokens(entry.ReservedTokens, used);
                else
                    config.RecordUsage(used);
            }

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return record;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < MaxConcurrencyRetries)
            {
                await ReloadAsync(ex);
            }
        }
    }

    private const int MaxConcurrencyRetries = 3;

    private static async Task ReloadAsync(DbUpdateConcurrencyException ex)
    {
        foreach (var entry in ex.Entries)
            await entry.ReloadAsync();
    }

    /// <summary>Hạn mức là theo tháng (UC-02): sang tháng mới (UTC) thì đưa UsedTokensCurrentMonth về 0.</summary>
    private static bool ResetIfNewMonth(WorkspaceAiConfig config)
    {
        if (WorkspaceAiConfig.IsCountedThisMonth(config.LastResetAt, config.CreatedAt, DateTime.UtcNow)) return false;

        config.ResetMonthlyUsage();
        return true;
    }
}
