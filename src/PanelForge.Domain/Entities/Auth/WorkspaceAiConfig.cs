using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Auth;

/// <summary>
/// Cấu hình AI Provider và hạn mức Token theo Workspace (UC-02, A-01, NFR-03).
/// </summary>
public class WorkspaceAiConfig : BaseEntity
{
    public Guid WorkspaceId { get; private set; }
    public string Provider { get; private set; } = "Google Gemini";
    public string? ApiKeyEncrypted { get; private set; }
    public bool IsEnabled { get; private set; } = true;
    public long MonthlyTokenQuota { get; private set; } = 1_000_000;
    public long UsedTokensCurrentMonth { get; private set; } = 0;
    public string AllowedModelsJson { get; private set; } = "[\"gemini-1.5-flash\",\"gemini-1.5-pro\"]";
    public DateTime? LastResetAt { get; private set; }

    public StudioWorkspace Workspace { get; private set; } = default!;

    private WorkspaceAiConfig() { }

    public static WorkspaceAiConfig Create(
        Guid workspaceId,
        string provider = "Google Gemini",
        string? apiKey = null,
        long monthlyTokenQuota = 1_000_000,
        string? allowedModelsJson = null)
    {
        return new WorkspaceAiConfig
        {
            WorkspaceId = workspaceId,
            Provider = string.IsNullOrWhiteSpace(provider) ? "Google Gemini" : provider.Trim(),
            ApiKeyEncrypted = apiKey?.Trim(),
            IsEnabled = true,
            MonthlyTokenQuota = Math.Max(0, monthlyTokenQuota),
            UsedTokensCurrentMonth = 0,
            AllowedModelsJson = string.IsNullOrWhiteSpace(allowedModelsJson)
                ? "[\"gemini-1.5-flash\",\"gemini-1.5-pro\"]"
                : allowedModelsJson.Trim(),
            LastResetAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void UpdateSettings(
        string provider,
        string? apiKey,
        bool isEnabled,
        long monthlyTokenQuota,
        string allowedModelsJson)
    {
        Provider = string.IsNullOrWhiteSpace(provider) ? Provider : provider.Trim();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            ApiKeyEncrypted = apiKey.Trim();
        }
        IsEnabled = isEnabled;
        MonthlyTokenQuota = Math.Max(0, monthlyTokenQuota);
        if (!string.IsNullOrWhiteSpace(allowedModelsJson))
        {
            AllowedModelsJson = allowedModelsJson.Trim();
        }
        UpdatedAt = DateTime.UtcNow;
    }

    public void RecordUsage(long tokensUsed)
    {
        if (tokensUsed > 0)
        {
            UsedTokensCurrentMonth += tokensUsed;
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public void ResetMonthlyUsage()
    {
        UsedTokensCurrentMonth = 0;
        LastResetAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public bool HasQuotaRemaining()
    {
        if (!IsEnabled) return false;
        if (MonthlyTokenQuota <= 0) return true; // unlimited
        return UsedTokensCurrentMonth < MonthlyTokenQuota;
    }
}
