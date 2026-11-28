using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Workspaces;

/// <summary>
/// Cấu hình AI Provider và hạn mức Token theo Workspace (UC-02, A-01, NFR-03).
/// </summary>
public class WorkspaceAiConfig : BaseEntity
{
    public Guid WorkspaceId { get; private set; }
    /// <summary>xmin: hai lần giữ chỗ/ghi token cùng lúc không ghi đè nhau (OPS-03).</summary>
    public uint RowVersion { get; private set; }
    public string Provider { get; private set; } = "Google Gemini";
    public string? ApiKeyEncrypted { get; private set; }
    /// <summary>API do Admin khai báo trong bảng ai_provider_credentials. Có giá trị thì thay cho Provider/ApiKeyEncrypted ở trên.</summary>
    public Guid? ProviderCredentialId { get; private set; }
    /// <summary>API riêng cho sinh ảnh (dùng ImageModel của credential đó). null = sinh ảnh theo API chữ/cấu hình hệ thống như trước.</summary>
    public Guid? ImageProviderCredentialId { get; private set; }
    public bool IsEnabled { get; private set; } = true;
    public long MonthlyTokenQuota { get; private set; } = 1_000_000;
    public long UsedTokensCurrentMonth { get; private set; } = 0;

    /// <summary>V1.2: quota ảnh sinh ra mỗi tháng, cùng bộ giới hạn và cùng chu kỳ reset với token. 0 = không giới hạn (giống token).</summary>
    public long MonthlyImageQuota { get; private set; } = 0;
    public long UsedImagesCurrentMonth { get; private set; } = 0;
    /// <summary>
    /// Model mặc định phải gồm model văn bản và ảnh mà AI service đang chạy, nếu không mọi lượt chạy bị chặn.
    /// Dòng 3.x đứng đầu: key của project Google mới bị từ chối dòng 2.5 ("no longer available to new users");
    /// giữ 2.5 cho key cũ vẫn còn quyền dùng.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultAllowedModels =
        ["gemini-3.8-flash", "gemini-3.1-pro-preview", "gemini-2.5-flash", "gemini-2.5-pro", "gemini-2.5-flash-image"];
    public const string DefaultAllowedModelsJson =
        "[\"gemini-3.8-flash\",\"gemini-3.1-pro-preview\",\"gemini-2.5-flash\",\"gemini-2.5-pro\",\"gemini-2.5-flash-image\"]";

    public string AllowedModelsJson { get; private set; } = DefaultAllowedModelsJson;
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
                ? DefaultAllowedModelsJson
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

    /// <summary>null = chưa chọn API (TASK-350: không còn key riêng theo Studio).</summary>
    public void UseProviderCredential(Guid? credentialId)
    {
        ProviderCredentialId = credentialId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UseImageProviderCredential(Guid? credentialId)
    {
        ImageProviderCredentialId = credentialId;
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

    /// <summary>
    /// OPS-03: giữ chỗ token ước lượng TRƯỚC khi gọi AI, để các yêu cầu chạy cùng lúc không cùng lọt qua ngưỡng.
    /// Phần giữ chỗ được trả lại/chốt bằng <see cref="SettleReservedTokens"/> khi biết số token thật.
    /// </summary>
    public bool TryReserveTokens(long tokens)
    {
        if (!HasQuotaRemaining()) return false;
        if (tokens <= 0) return true;
        if (MonthlyTokenQuota > 0 && UsedTokensCurrentMonth + tokens > MonthlyTokenQuota) return false;
        UsedTokensCurrentMonth += tokens;
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>Chốt lần gọi đã giữ chỗ: trừ phần đã giữ, cộng số token thật (0 nếu gọi lỗi).</summary>
    public void SettleReservedTokens(long reserved, long actualUsed)
    {
        UsedTokensCurrentMonth = Math.Max(0, UsedTokensCurrentMonth - Math.Max(0, reserved) + Math.Max(0, actualUsed));
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Admin cấp lại token/tháng (OPS-21, OPS-23). 0 = không giới hạn.</summary>
    public void SetMonthlyTokenQuota(long quota)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(quota);
        MonthlyTokenQuota = quota;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetMonthlyImageQuota(long quota)
    {
        MonthlyImageQuota = Math.Max(0, quota);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Còn đủ quota để sinh thêm <paramref name="count"/> ảnh không (gọi TRƯỚC khi sinh, như HasQuotaRemaining với token).</summary>
    public bool HasImageQuotaFor(long count)
    {
        if (!IsEnabled) return false;
        if (MonthlyImageQuota <= 0) return true; // unlimited
        return UsedImagesCurrentMonth + count <= MonthlyImageQuota;
    }

    /// <summary>Ghi nhận ảnh đã sinh thành công (gọi SAU khi sinh, như RecordUsage với token).</summary>
    public void RecordImages(long count)
    {
        if (count > 0)
        {
            UsedImagesCurrentMonth += count;
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public void ResetMonthlyUsage()
    {
        UsedTokensCurrentMonth = 0;
        UsedImagesCurrentMonth = 0;
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
