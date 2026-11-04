using System.Text;

namespace PanelForge.AI.Contracts;

/// <summary>Task Copilot (sync) mà AI service hỗ trợ. Backend chọn task theo vai trò; client không gửi agent/role.</summary>
public static class CopilotTasks
{
    public const string WriterRewrite = "writer.rewrite";
    public const string LettererShorten = "letterer.shorten";
    public const string PublisherMetadata = "publisher.metadata";

    public static readonly string[] All = [WriterRewrite, LettererShorten, PublisherMetadata];
    /// <summary>Task làm trên MỘT dòng thoại (cần Line); còn lại làm ở cấp Series (cần Series).</summary>
    public static bool IsLineTask(string task) => task is WriterRewrite or LettererShorten;
}

/// <summary>Backend → AI service: một tác vụ Copilot trên MỘT dòng thoại. ApiKey chỉ trong bộ nhớ, không log.</summary>
public sealed record CopilotRequest(
    string Task,
    string Provider,
    string Model,
    string? ApiKey,
    CopilotLine? Line,
    IReadOnlyList<BibleEntryContext> Bible,
    int? MaxChars,
    string? Instruction,
    int SchemaVersion = AiProtocol.SchemaVersion,
    CopilotSeriesContext? Series = null)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"Task = {Task}, Provider = {Provider}, Model = {Model}, ApiKey = ***");
        return true;
    }
}

/// <summary>Dòng thoại cần xử lý + vài dòng xung quanh (chỉ để giữ mạch, không được sửa).</summary>
public sealed record CopilotLine(
    Guid LineId,
    Guid? SpeakerId,
    string Dialogue,
    string? StageDirection,
    IReadOnlyList<string> Neighbors);

/// <summary>Context cấp Series cho publisher.metadata: thông tin hiện tại + dàn ý các chương (không gửi toàn bộ script).</summary>
public sealed record CopilotSeriesContext(
    string Title,
    string? Synopsis,
    string Genre,
    string Format,
    string? StoryArc,
    IReadOnlyList<string> ChapterOutlines);

/// <summary>Gợi ý phát hành. Chỉ Synopsis + Genre được ghi vào Series khi Accept; còn lại để Publisher dùng khi đăng.</summary>
public sealed record ReleaseMetadata(string Logline, string Synopsis, string Genre, string Teaser, string AgeRating);

/// <summary>AI service → Backend. Text đã qua kiểm tra của code (<see cref="CopilotRules.Validate"/>).</summary>
public sealed record CopilotResponse(
    bool Succeeded,
    string? Text,
    string? Reason,
    string PromptVersion,
    string PromptRef,
    long? PromptTokens,
    long? CompletionTokens,
    int RepairAttempts,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    string? ErrorCode,
    string? ErrorMessage,
    ReleaseMetadata? Metadata = null);

/// <summary>Payload của AiSuggestion kind SeriesMetadata. Original* = giá trị Series lúc AI chạy (kiểm "đã đổi" khi Accept).</summary>
public sealed record SeriesMetadataSuggestion(
    int SchemaVersion,
    Guid RunId,
    Guid SeriesId,
    string? OriginalSynopsis,
    string OriginalGenre,
    ReleaseMetadata Metadata,
    string Reason);

/// <summary>Payload của AiSuggestion kind LineRewrite (Writer viết lại giọng / Letterer rút gọn một dòng).</summary>
public sealed record LineRewriteSuggestion(
    int SchemaVersion,
    Guid RunId,
    Guid SceneId,
    string Task,
    Guid SourceScriptLineId,
    string OriginalText,
    string SuggestedText,
    string Reason,
    int? MaxChars);

public static class CopilotRules
{
    public const int MaxInstructionLength = 500;
    public const int MaxCharsLimit = 500;

    public static readonly string[] AgeRatings = ["All", "13+", "16+", "18+"];

    /// <summary>Kiểm tra tất định gợi ý phát hành (độ dài khớp cột DB: genre varchar(100)).</summary>
    public static IReadOnlyList<string> ValidateMetadata(ReleaseMetadata? m)
    {
        if (m is null) return ["thiếu metadata."];
        var errors = new List<string>();
        void Check(string name, string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) errors.Add($"{name} rỗng.");
            else if (value.Trim().Length > max) errors.Add($"{name} dài quá {max} ký tự.");
        }
        Check("logline", m.Logline, 200);
        Check("synopsis", m.Synopsis, 2000);
        Check("genre", m.Genre, 100);
        Check("teaser", m.Teaser, 300);
        if (!AgeRatings.Contains(m.AgeRating?.Trim())) errors.Add($"ageRating phải là một trong: {string.Join(", ", AgeRatings)}.");
        return errors;
    }

    /// <summary>Kiểm tra tất định output Copilot; dùng chung ở AI service (trước khi trả) và Backend (khi Accept).</summary>
    public static IReadOnlyList<string> Validate(string task, string original, string? suggested, int? maxChars)
    {
        var errors = new List<string>();
        var text = suggested?.Trim() ?? "";
        var source = original.Trim();
        if (text.Length == 0) errors.Add("text rỗng.");
        else if (text == source) errors.Add("text trùng nguyên văn bản gốc.");
        if (task == CopilotTasks.LettererShorten)
        {
            if (text.Length >= source.Length) errors.Add("text phải ngắn hơn bản gốc.");
            if (maxChars is { } m && text.Length > m) errors.Add($"text dài {text.Length} ký tự, vượt giới hạn {m}.");
        }
        else if (text.Length > Math.Max(source.Length * 2, 200))
            errors.Add("text dài bất thường so với bản gốc.");
        return errors;
    }
}
