namespace PanelForge.Application.Features.AiAgents.Models;

/// <summary>
/// Cấu hình phía Backend cho tính năng AI Multi-Agent (section "AiService" trong appsettings / user-secrets).
/// ServiceKey là khoá dịch vụ giữa hai service — chỉ đặt qua user-secrets, không đưa vào appsettings.json.
/// </summary>
public sealed class AiAgentSettings
{
    public const string SectionName = "AiService";

    /// <summary>Mock (mặc định, không cần API key) hoặc Gemini.</summary>
    public string Provider { get; set; } = "Mock";
    public string GeminiModel { get; set; } = "gemini-2.5-flash";
    public string ServiceUrl { get; set; } = "http://127.0.0.1:5055";
    public string? ServiceKey { get; set; }
    public int MaxRevisionRounds { get; set; } = 2;
    /// <summary>Run Queued/Running không có hoạt động quá thời gian này sẽ bị đánh dấu Interrupted.</summary>
    public int RunStaleAfterSeconds { get; set; } = 300;
    public int ServiceTimeoutSeconds { get; set; } = 10;

    public bool IsMock => string.Equals(Provider, "Mock", StringComparison.OrdinalIgnoreCase);
}