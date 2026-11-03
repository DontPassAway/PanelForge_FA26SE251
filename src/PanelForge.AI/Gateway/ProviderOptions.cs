namespace PanelForge.AI.Gateway;

/// <summary>Cấu hình gọi model (section "Gateway"). Không chứa API key: key đến theo từng run từ Backend và chỉ nằm trong bộ nhớ.</summary>
public sealed class ProviderOptions
{
    public const string SectionName = "Gateway";

    public string GeminiOpenAiEndpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta/openai/";
    public string GeminiModelsUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/models/";

    /// <summary>Timeout cho MỘT lần gọi model.</summary>
    public int CallTimeoutSeconds { get; set; } = 60;
    /// <summary>Số lần thử lại (không tính lần đầu) khi lỗi tạm thời (timeout, 408/429/5xx).</summary>
    public int MaxRetries { get; set; } = 3;
    public int RetryBaseDelaySeconds { get; set; } = 4;
    /// <summary>
    /// Giãn cách tối thiểu CỐ ĐỊNH giữa hai lần gọi model (toàn service). Mặc định 0: không chờ, chỉ nghỉ khi provider trả 429
    /// (xem RateLimitCooldown*). Đặt ~6000 nếu muốn chủ động giữ dưới ~10 request/phút của free tier.
    /// </summary>
    public int MinCallIntervalMs { get; set; } = 0;
    /// <summary>Sau 429 (không có Retry-After): nghỉ base × 2^(số lần 429 liên tiếp − 1) giây, tối đa Max; gọi thành công thì reset.</summary>
    public int RateLimitCooldownSeconds { get; set; } = 8;
    public int RateLimitCooldownMaxSeconds { get; set; } = 60;
    /// <summary>Mở mạch khi tỉ lệ lỗi ≥ FailureRatio trong SamplingSeconds với tối thiểu MinimumThroughput lần gọi.</summary>
    public double BreakerFailureRatio { get; set; } = 0.5;
    public int BreakerSamplingSeconds { get; set; } = 60;
    public int BreakerMinimumThroughput { get; set; } = 4;
    public int BreakerBreakSeconds { get; set; } = 30;

    public float Temperature { get; set; } = 0.3f;
    public int MaxOutputTokens { get; set; } = 4096;
}