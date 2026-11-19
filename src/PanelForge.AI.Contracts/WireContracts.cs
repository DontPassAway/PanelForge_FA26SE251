using System.Text;

namespace PanelForge.AI.Contracts;

/// <summary>Backend → AI service: giao một run. ApiKey chỉ tồn tại trong bộ nhớ, không bao giờ được log.</summary>
public sealed record AiRunJob(
    Guid RunId,
    string Provider,
    string Model,
    string? ApiKey,
    int MaxRevisionRounds,
    int SchemaVersion = AiProtocol.SchemaVersion)
{
    // Record tự sinh ToString() in mọi property → ẩn ApiKey để không lọt vào log/exception.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"RunId = {RunId}, Provider = {Provider}, Model = {Model}, ApiKey = ***");
        return true;
    }
}

public sealed record AiRunAck(bool Accepted, string? ErrorCode = null, string? Message = null);

/// <summary>AI service → Backend: lấy context của run. Backend dựng từ snapshot đã lưu ở đầu run.</summary>
public sealed record AgentContext(
    Guid RunId,
    Guid SeriesId,
    Guid ChapterId,
    decimal ChapterNumber,
    Guid SceneId,
    string SceneHeading,
    string? SceneSummary,
    string Brief,
    string ReadingDirection,
    long ChapterVersion,
    string Provider,
    string Model,
    int MaxRevisionRounds,
    IReadOnlyList<BibleEntryContext> Bible,
    IReadOnlyList<Guid> BibleRevisionIds,
    IReadOnlyList<BaselineScriptLine> BaselineScript,
    // Các trường dưới có mặc định để run/JSON cũ vẫn đọc được (run cũ = FullScene, không có context riêng).
    string WorkflowType = AiProtocol.WorkflowFullScene,
    StoryboardLayout? ExistingLayout = null,
    ArtistContext? Artist = null,
    string? SeriesStoryArc = null,
    string? ChapterOutline = null)
{
    /// <summary>Dữ kiện đầu vào code đã kiểm: cơ sở để <see cref="WorkflowRules"/> quyết định hành động hợp lệ.</summary>
    public WorkflowInputs Inputs => new(
        BaselineScript.Count > 0, ExistingLayout is not null, Artist?.Panels.Count > 0);
}

public sealed record BibleEntryContext(
    Guid EntryId,
    string Code,
    string Category,
    string Name,
    string Description,
    Guid RevisionId,
    int RevisionVersion,
    int EffectiveFromChapter,
    string? Details = null)
{
    /// <summary>Văn bản Bible dùng làm căn cứ trích dẫn (tên + mô tả + sự thật có cấu trúc). AI service và Backend dùng chung.</summary>
    public string EvidenceText => string.IsNullOrWhiteSpace(Details) ? $"{Name}\n{Description}" : $"{Name}\n{Description}\n{Details}";
}

public sealed record BaselineScriptLine(Guid LineId, int Order, Guid? SpeakerId, string? Dialogue, string? StageDirection);

/// <summary>AI service → Backend: kết quả MỘT step. Backend lưu rồi mới trả ack; AI service chỉ chạy bước kế khi ack.ShouldContinue.</summary>
public sealed record AgentStepReport(
    int StepIndex,
    string Agent,
    int Round,
    int? ParentStepIndex,
    bool Succeeded,
    string Provider,
    string Model,
    string PromptVersion,
    string PromptRef,
    string ParametersJson,
    string InputJson,
    string? OutputJson,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    long? PromptTokens,
    long? CompletionTokens,
    int RepairAttempts,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record AgentStepAck(Guid StepId, bool Duplicate, bool ShouldContinue, string RunStatus, string? Message = null);

// ── Context riêng theo loại workflow (do Backend dựng từ DB, bất biến trong run) ───────────────
public sealed record ArtistPanelContext(
    Guid PanelId,
    int PageNumber,
    int PanelNumber,
    int ReadingOrder,
    string Camera,
    string Description,
    IReadOnlyList<Guid> CharacterIds,
    IReadOnlyList<Guid> SourceScriptLineIds);

public sealed record StyleRuleContext(string Name, string RuleType, string? Description, string Severity);

public sealed record ArtistContext(
    Guid LayoutRevisionId,
    IReadOnlyList<ArtistPanelContext> Panels,
    IReadOnlyList<StyleRuleContext> StyleRules);
