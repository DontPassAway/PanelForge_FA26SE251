namespace PanelForge.Application.Features.AiAgents.Models;

/// <summary>
/// Cấu hình phía Backend cho tính năng AI Multi-Agent (section "AiService" trong appsettings / user-secrets).
/// ServiceKey là khoá dịch vụ giữa hai service — chỉ đặt qua user-secrets, không đưa vào appsettings.json.
/// </summary>
public sealed class AiAgentSettings
{
    public const string SectionName = "AiService";

    public string GeminiModel { get; set; } = "gemini-3.8-flash";
    public string ServiceUrl { get; set; } = "http://127.0.0.1:5055";
    public string? ServiceKey { get; set; }
    public int MaxRevisionRounds { get; set; } = 2;
    /// <summary>Run Queued/Running không có hoạt động quá thời gian này sẽ bị đánh dấu Interrupted.</summary>
    public int RunStaleAfterSeconds { get; set; } = 300;
    public int ServiceTimeoutSeconds { get; set; } = 10;
    /// <summary>Copilot sync: timeout một lần gọi và số lượt tối đa mỗi phút cho MỘT Series.</summary>
    public int CopilotTimeoutSeconds { get; set; } = 30;
    public int CopilotCallsPerMinutePerSeries { get; set; } = 10;
    /// <summary>OPS-03: số token giữ chỗ trước mỗi lượt Copilot, chốt lại theo số thật sau khi gọi.</summary>
    public long CopilotReservedTokens { get; set; } = 4000;

    /// <summary>Sinh ảnh thumbnail: Gemini (dùng key Workspace, nhận ảnh tham chiếu) hoặc Cloudflare (Workers AI text-to-image, token ở AI service).</summary>
    public string ImageProvider { get; set; } = "Gemini";
    public string GeminiImageModel { get; set; } = "gemini-2.5-flash-image";
    public string CloudflareImageModel { get; set; } = "@cf/black-forest-labs/flux-1-schnell";
    public int ImageTimeoutSeconds { get; set; } = 100;
    /// <summary>
    /// Phong cách chèn trước prompt ảnh khi prompt chưa nói tới manga/comic (prompt đã duyệt từ trước, model ảnh mặc định ra ảnh chụp).
    /// Để trống để tắt. Style rules của Series vẫn nằm trong prompt do Artist Agent soạn.
    /// </summary>
    public string ThumbnailStylePrefix { get; set; } =
        "Manga comic panel, black-and-white ink line art, screentone shading, expressive anime-style faces, dynamic comic composition, hand-drawn illustration, not a photograph.";
    /// <summary>Số job sinh ảnh chạy đồng thời tối đa cho MỘT Series.</summary>
    public int ImageJobsPerSeries { get; set; } = 2;

    /// <summary>Chế độ demo: hiện workflow FullScene (Script → Storyboard → Editor trong một lần chạy) trong danh sách nhiệm vụ.
    /// Tắt mặc định; FullScene vẫn chạy được qua API khi gọi trực tiếp.</summary>
    public bool ShowFullSceneWorkflow { get; set; }

}