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
    /// <summary>Copilot sync: timeout một lần gọi và số lượt tối đa mỗi phút cho MỘT Series.</summary>
    public int CopilotTimeoutSeconds { get; set; } = 15;
    public int CopilotCallsPerMinutePerSeries { get; set; } = 10;

    /// <summary>Sinh ảnh thumbnail: Gemini (dùng key Workspace, nhận ảnh tham chiếu) hoặc Cloudflare (Workers AI text-to-image, token ở AI service).</summary>
    public string ImageProvider { get; set; } = "Gemini";
    public string GeminiImageModel { get; set; } = "gemini-2.5-flash-image";
    public string CloudflareImageModel { get; set; } = "@cf/black-forest-labs/flux-1-schnell";
    public int ImageTimeoutSeconds { get; set; } = 100;
    /// <summary>Số job sinh ảnh chạy đồng thời tối đa cho MỘT Series.</summary>
    public int ImageJobsPerSeries { get; set; } = 2;

    /// <summary>Chế độ demo: hiện workflow FullScene (Script → Storyboard → Editor trong một lần chạy) trong danh sách nhiệm vụ.
    /// Tắt mặc định; FullScene vẫn chạy được qua API khi gọi trực tiếp.</summary>
    public bool ShowFullSceneWorkflow { get; set; }

    public bool IsMock => string.Equals(Provider, "Mock", StringComparison.OrdinalIgnoreCase);
}