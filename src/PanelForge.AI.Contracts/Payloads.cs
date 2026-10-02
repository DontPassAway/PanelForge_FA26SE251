namespace PanelForge.AI.Contracts;

// ── Supervisor (Gemini điều phối) ────────────────────────────────────────────
/// <summary>
/// Quyết định điều phối của Supervisor: gọi agent nào tiếp theo (hoặc StopForHuman / Finish).
/// AllowedActions do CODE tính (WorkflowRules) — Supervisor chỉ được chọn trong danh sách này.
/// </summary>
public sealed record SupervisorDecision(
    int SchemaVersion,
    Guid RunId,
    int Round,
    string Action,
    string Reason,
    string? Instruction,
    IReadOnlyList<string> AllowedActions,
    bool GuardrailOverridden);

// ── Writer ───────────────────────────────────────────────────────────────────
/// <summary>LineId do ứng dụng cấp (không phải model). Khi Accept, ScriptLine được tạo với đúng Id này.</summary>
public sealed record ScriptDraftLine(Guid LineId, int Order, Guid? SpeakerId, string? Dialogue, string? StageDirection);

public sealed record ScriptDraft(
    int SchemaVersion,
    Guid RunId,
    Guid SceneId,
    int Round,
    IReadOnlyList<ScriptDraftLine> Lines,
    IReadOnlyList<Guid> BibleRevisionIds,
    string? RevisionNotes);

// ── Continuity ───────────────────────────────────────────────────────────────
public sealed record BibleEvidence(Guid BibleEntryId, Guid BibleRevisionId, string Quote);

public sealed record ContinuityFinding(
    string FindingId,
    string Severity,
    string Category,
    string Description,
    IReadOnlyList<Guid> ScriptLineIds,
    IReadOnlyList<BibleEvidence> Evidence,
    string? SuggestedFix);

public sealed record ContinuityReport(
    int SchemaVersion,
    Guid RunId,
    int Round,
    IReadOnlyList<ContinuityFinding> Findings,
    string Summary)
{
    public bool HasBlockingFindings => Findings.Any(f => f.Severity == AiProtocol.SeverityBlocking);
}

// ── Storyboard ───────────────────────────────────────────────────────────────
public sealed record Bbox(double X, double Y, double W, double H);

/// <summary>Không có trường elements: Storyboard không tạo lettering trong demo.</summary>
public sealed record LayoutPanel(
    int PanelNumber,
    int ReadingOrder,
    Bbox Bbox,
    string CoordinateSpace,
    string Camera,
    string Description,
    IReadOnlyList<Guid> CharacterIds,
    IReadOnlyList<Guid> SourceScriptLineIds);

public sealed record LayoutPage(int PageNumber, IReadOnlyList<LayoutPanel> Panels);

public sealed record StoryboardLayout(
    int SchemaVersion,
    Guid RunId,
    Guid SceneId,
    string ReadingDirection,
    IReadOnlyList<LayoutPage> Pages,
    string? Notes);

// ── Editor ───────────────────────────────────────────────────────────────────
public sealed record ChecklistItem(string Name, bool Passed, string Detail);

public sealed record ReviewItem(string Target, string Severity, string Comment, IReadOnlyList<Guid> ScriptLineIds);

/// <summary>Checklist do code gom; Verdict chỉ là khuyến nghị — người mới là bên quyết định.</summary>
public sealed record ReviewReport(
    int SchemaVersion,
    Guid RunId,
    string Verdict,
    string Summary,
    IReadOnlyList<ChecklistItem> Checklist,
    IReadOnlyList<ReviewItem> Items,
    string? DraftRejectComment);

// ── Artist ───────────────────────────────────────────────────────────────────
/// <summary>PanelId, PageNumber, PanelNumber, ReferenceIds do CODE điền/kiểm; model chỉ viết Prompt + ConsistencyNotes + chọn khoá tham chiếu.</summary>
public sealed record ArtistPanelPrompt(
    Guid PanelId,
    int PageNumber,
    int PanelNumber,
    string Prompt,
    IReadOnlyList<Guid> ReferenceIds,
    string? ConsistencyNotes);

/// <summary>Chỉ soạn prompt ảnh; không sinh ảnh. LayoutRevisionId = layout đã duyệt làm nền (kiểm stale khi Accept).</summary>
public sealed record ArtistPromptSet(
    int SchemaVersion,
    Guid RunId,
    Guid SceneId,
    Guid LayoutRevisionId,
    IReadOnlyList<ArtistPanelPrompt> Panels,
    string? StyleSummary);

// ── Letterer ─────────────────────────────────────────────────────────────────
/// <summary>
/// Đề xuất rút gọn một dòng thoại. SourceScriptLineId, TargetElementId, PanelId, OriginalText, MaxChars, LimitSource do CODE điền từ context;
/// WithinEstimatedLimit là ƯỚC LƯỢNG của code (không phải kết quả render, không phải nhận định của LLM).
/// </summary>
public sealed record LetteringSuggestion(
    Guid SourceScriptLineId,
    Guid? TargetElementId,
    Guid? PanelId,
    string OriginalText,
    string SuggestedText,
    string Reason,
    int MaxChars,
    string LimitSource,
    bool WithinEstimatedLimit);

public sealed record LetteringSuggestionSet(
    int SchemaVersion,
    Guid RunId,
    Guid SceneId,
    IReadOnlyList<LetteringSuggestion> Suggestions,
    string? Summary);

// ── Producer ─────────────────────────────────────────────────────────────────
public sealed record ProductionRisk(string Severity, string Description, IReadOnlyList<string> SourceFactKeys);

public sealed record ProductionRecommendation(
    string Action, string Rationale, IReadOnlyList<string> SourceFactKeys, string? SuggestedOwnerRole);

/// <summary>Báo cáo tham khảo (không phải đề xuất áp dụng). Facts/DataGaps do code sao chép từ snapshot để báo cáo tự đủ nguồn.</summary>
public sealed record ProductionReport(
    int SchemaVersion,
    Guid RunId,
    string Headline,
    string Summary,
    IReadOnlyList<ProductionRisk> Risks,
    IReadOnlyList<ProductionRecommendation> Recommendations,
    IReadOnlyList<string> DataGaps,
    IReadOnlyList<ProgressFact> Facts);
