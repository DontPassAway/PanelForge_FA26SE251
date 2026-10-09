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
    /// Giãn cách tối thiểu giữa hai lần gọi model (toàn service). Free tier Gemini giới hạn ~10 request/phút, mà Supervisor
    /// làm số lần gọi gần gấp đôi (mỗi agent kèm một quyết định) → mặc định 6,5 giây để không bị 429. Đặt 0 nếu có quota cao.
    /// </summary>
    public int MinCallIntervalMs { get; set; } = 6500;
    /// <summary>Mở mạch khi tỉ lệ lỗi ≥ FailureRatio trong SamplingSeconds với tối thiểu MinimumThroughput lần gọi.</summary>
    public double BreakerFailureRatio { get; set; } = 0.5;
    public int BreakerSamplingSeconds { get; set; } = 60;
    public int BreakerMinimumThroughput { get; set; } = 4;
    public int BreakerBreakSeconds { get; set; } = 30;

    public float Temperature { get; set; } = 0.3f;
    public int MaxOutputTokens { get; set; } = 4096;
}