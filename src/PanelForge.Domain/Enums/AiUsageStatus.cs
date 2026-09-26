namespace PanelForge.Domain.Enums;

/// <summary>Kết quả của một lần gọi AI (E-29 AIUsageRecord).</summary>
public enum AiUsageStatus
{
    Succeeded = 1,
    Failed = 2,
    /// <summary>Bị chặn vì workspace đã hết hạn mức token tháng (UC-02 luồng phụ).</summary>
    QuotaExceeded = 3,
    /// <summary>Provider lỗi / không phản hồi (UC-02 luồng phụ).</summary>
    ProviderUnavailable = 4,
    /// <summary>Bị chặn vì AI chưa được cấu hình hoặc đã bị tắt cho workspace.</summary>
    Disabled = 5
}
