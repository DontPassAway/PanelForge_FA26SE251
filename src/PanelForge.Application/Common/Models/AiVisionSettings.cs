namespace PanelForge.Application.Common.Models;

/// <summary>
/// Cấu hình cho dịch vụ AI Vision kiểm tra nhất quán hình ảnh (Bước A5, OpenAI-compatible).
/// </summary>
public sealed class AiVisionSettings
{
    public const string SectionName = "AiVisionSettings";

    /// <summary>Nhà cung cấp AI (mặc định "Gemini").</summary>
    public string Provider { get; set; } = "Gemini";

    /// <summary>API Key cấu hình cấp hệ thống (dùng fallback khi Workspace chưa cấu hình key riêng).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Tên model Vision (mặc định "gemini-2.5-flash").</summary>
    public string ModelName { get; set; } = "gemini-2.5-flash";

    /// <summary>Endpoint chat completions OpenAI-compatible.</summary>
    public string EndpointUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions";

    /// <summary>Ngưỡng tương đồng tối thiểu để đạt tính nhất quán (mặc định 0.75).</summary>
    public decimal SimilarityThreshold { get; set; } = 0.75m;
}
