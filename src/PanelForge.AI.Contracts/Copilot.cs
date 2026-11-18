using System.Text;

namespace PanelForge.AI.Contracts;

/// <summary>Task Copilot (sync) mà AI service hỗ trợ. Backend chọn task theo vai trò; client không gửi agent/role.</summary>
public static class CopilotTasks
{
    public const string WriterRewrite = "writer.rewrite";
    public const string LettererShorten = "letterer.shorten";
    public const string PublisherMetadata = "publisher.metadata";
    public const string BibleStubDraft = "bible.stub_draft";
    public const string ImpactRipple = "impact.ripple";

    public static readonly string[] All = [WriterRewrite, LettererShorten, PublisherMetadata, BibleStubDraft, ImpactRipple];
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
    CopilotSeriesContext? Series = null,
    BibleStubContext? Stub = null,
    ImpactRippleContext? Ripple = null)
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
    ReleaseMetadata? Metadata = null,
    BibleStubDraft? Stub = null,
    string? ImpactDecision = null);

public sealed record ImpactLineChange(string Type, string? Label, string? Before, string? After);
public sealed record ImpactRippleContext(string StageName, string Role, int PageNumber, IReadOnlyList<ImpactLineChange> Changes);

/// <summary>Payload của AiSuggestion kind ImpactRipple. Accept mới thực hiện Keep hoặc Rework qua Impact Review.</summary>
public sealed record ImpactRippleSuggestion(int SchemaVersion, Guid RunId, Guid TaskId, string Decision, string Reason,
    IReadOnlyList<ImpactLineChange> Changes);

/// <summary>Dòng kịch bản có nhắc tới mục Bible nháp: nguồn duy nhất AI được dùng.</summary>
public sealed record BibleStubEvidence(Guid LineId, string Text);
public sealed record BibleStubContext(string Name, string Category, IReadOnlyList<BibleStubEvidence> Evidence);
public sealed record BibleStubCitation(string Claim, Guid LineId, string Quote);
public sealed record BibleStubDraft(string Description, string? Subtitle, IReadOnlyList<BibleStubCitation> Citations);

/// <summary>Payload của AiSuggestion kind BibleStub. Accept không ghi Bible; người viết sửa rồi lưu qua API định nghĩa mục.</summary>
public sealed record BibleStubSuggestion(int SchemaVersion, Guid RunId, Guid EntryId, string Name, BibleStubDraft Draft, string Reason);

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
/// <summary>Bản rút gọn cho chữ trên MỘT bóng thoại. Accept chỉ đổi chữ của element, không đổi ScriptLine nên dùng được khi kịch bản Frozen.</summary>
public sealed record BubbleShortenSuggestion(
    int SchemaVersion,
    Guid RunId,
    Guid SceneId,
    string Task,
    Guid PageId,
    Guid ElementId,
    int ElementVersion,
    string OriginalText,
    string SuggestedText,
    string Reason,
    int MaxChars);

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

    /// <summary>Chữ đang hiển thị của bóng thoại.</summary>
    public static string? BubbleText(string? renderedText, string? content) =>
        string.IsNullOrWhiteSpace(renderedText) ? content : renderedText;
    public const int MaxCharsLimit = 500;

    public static readonly string[] AgeRatings = ["All", "13+", "16+", "18+"];

    public const string ImpactKeep = "Keep", ImpactRework = "Rework";

    public static IReadOnlyList<string> ValidateImpact(string? decision, string? reason)
    {
        var errors = new List<string>();
        if (decision is not (ImpactKeep or ImpactRework)) errors.Add("decision phải là Keep hoặc Rework.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 300) errors.Add("reason phải có nội dung và tối đa 300 ký tự.");
        return errors;
    }

    /// <summary>Mọi trích dẫn phải chỉ tới dòng được cung cấp và là đoạn nguyên văn của dòng đó.</summary>
    public static IReadOnlyList<string> ValidateStubDraft(BibleStubDraft? draft, BibleStubContext context)
    {
        if (draft is null) return ["thiếu hồ sơ nháp."];
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(draft.Description) || draft.Description.Length > 2000)
            errors.Add("description phải có nội dung và tối đa 2000 ký tự.");
        if (draft.Citations is not { Count: > 0 and <= 20 }) errors.Add("citations phải có từ 1 đến 20 phần tử.");
        foreach (var c in draft.Citations ?? [])
        {
            var source = context.Evidence.FirstOrDefault(e => e.LineId == c.LineId);
            if (source is null) errors.Add($"citation trỏ tới dòng không được cung cấp: {c.LineId}.");
            else if (string.IsNullOrWhiteSpace(c.Claim) || string.IsNullOrWhiteSpace(c.Quote)
                     || !source.Text.Contains(c.Quote.Trim(), StringComparison.OrdinalIgnoreCase))
                errors.Add($"citation của dòng {c.LineId} không phải đoạn nguyên văn.");
        }
        return errors;
    }

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
            if (maxChars is { } m && text.Length > m) errors.Add($"text dài {text.Length} ký tự, vượt giới hạn {m}; cần bớt ít nhất {text.Length - m} ký tự.");
        }
        else if (text.Length > Math.Max(source.Length * 2, 200))
            errors.Add("text dài bất thường so với bản gốc.");
        return errors;
    }
}
