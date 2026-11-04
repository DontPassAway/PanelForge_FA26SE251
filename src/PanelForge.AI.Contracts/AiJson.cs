using System.Text.Json;
using System.Text.Json.Serialization;

namespace PanelForge.AI.Contracts;

/// <summary>Cấu hình JSON duy nhất cho mọi payload/HTTP giữa hai service (camelCase, enum dạng chuỗi).</summary>
public static class AiJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        // Giữ tiếng Việt dạng đọc được trong payload lưu DB (không escape \uXXXX)
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}

public static class AiProtocol
{
    /// <summary>Phiên bản schema của mọi payload (ScriptDraft, ContinuityReport, ...).</summary>
    public const int SchemaVersion = 1;

    public const string ServiceKeyHeader = "X-Service-Key";
    public const string ProviderMock = "Mock";
    public const string ProviderGemini = "Gemini";

    public const string AgentSupervisor = "Supervisor";
    public const string AgentWriter = "Writer";
    public const string AgentContinuity = "Continuity";
    public const string AgentStoryboard = "Storyboard";
    public const string AgentEditor = "Editor";
    public const string AgentArtist = "Artist";

    /// <summary>Loại workflow = nhiệm vụ. Run cũ (trước khi có loại) được đọc như <see cref="WorkflowFullScene"/>.</summary>
    public const string WorkflowFullScene = "FullScene";   // Writer → Continuity (vòng sửa) → Storyboard → Editor
    public const string WorkflowScript = "Script";         // Writer → Continuity (vòng sửa)
    public const string WorkflowStoryboard = "Storyboard"; // Continuity (script có sẵn) → Storyboard → Editor
    public const string WorkflowArtist = "Artist";         // Artist soạn prompt ảnh cho panel của layout đã duyệt
    public const string WorkflowEditor = "Editor";         // Continuity (script có sẵn) → Editor

    public static readonly string[] AllWorkflows =
        [WorkflowFullScene, WorkflowScript, WorkflowStoryboard, WorkflowArtist, WorkflowEditor];

    public const string SeverityBlocking = "Blocking";
    public const string SeverityWarning = "Warning";
    public const string SeverityInfo = "Info";

    public const string PagePixelSpace = "page-normalized";

}
