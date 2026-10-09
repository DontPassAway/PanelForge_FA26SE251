using System.Text;
using PanelForge.AI.Contracts;

namespace PanelForge.AI.Agents;

// ── Output THÔ của model (dùng khoá L1/B1 và code, không dùng GUID) ───────────────
// Code của service đổi sang contract có GUID do ứng dụng cấp; model không bao giờ tự bịa ID.
public sealed record WriterOutput(List<WriterLine>? Lines, string? RevisionNotes);
public sealed record WriterLine(string? Speaker, string? Dialogue, string? StageDirection);

public sealed record ContinuityOutput(List<ContinuityOutFinding>? Findings, string? Summary);
public sealed record ContinuityOutFinding(
    string? Severity, string? Category, string? Description, List<string>? LineKeys,
    List<ContinuityOutEvidence>? Evidence, string? SuggestedFix);
public sealed record ContinuityOutEvidence(string? BibleKey, string? Quote);

public sealed record StoryboardOutput(List<StoryboardOutPage>? Pages, string? Notes);
public sealed record StoryboardOutPage(List<StoryboardOutPanel>? Panels);
public sealed record StoryboardOutPanel(Bbox? Bbox, string? Camera, string? Description, List<string>? Characters, List<string>? LineKeys);

public sealed record EditorOutput(string? Verdict, string? Summary, List<EditorOutItem>? Items, string? DraftRejectComment);
public sealed record EditorOutItem(string? Target, string? Severity, string? Comment, List<string>? LineKeys);

public sealed record ArtistOutput(List<ArtistOutPanel>? Panels, string? StyleSummary);
public sealed record ArtistOutPanel(string? PanelKey, string? Prompt, List<string>? ReferenceKeys, string? ConsistencyNotes);

public sealed record LettererOutput(List<LettererOutItem>? Suggestions, string? Summary);
public sealed record LettererOutItem(string? LineKey, string? SuggestedText, string? Reason);

public sealed record ProducerOutput(
    string? Headline, string? Summary, List<ProducerOutRisk>? Risks, List<ProducerOutRec>? Recommendations);
public sealed record ProducerOutRisk(string? Severity, string? Description, List<string>? SourceFactKeys);
public sealed record ProducerOutRec(string? Action, string? Rationale, List<string>? SourceFactKeys, string? SuggestedOwnerRole);

/// <summary>Khoá ngắn dùng trong prompt ↔ ID thật. Bible: B1..Bn theo thứ tự context; script: L{order}.</summary>
public sealed class KeyMaps
{
    public IReadOnlyDictionary<string, BibleEntryContext> Bible { get; }
    public IReadOnlyDictionary<string, BibleEntryContext> CharactersByCode { get; }

    public KeyMaps(AgentContext ctx)
    {
        Bible = ctx.Bible.Select((b, i) => (Key: $"B{i + 1}", Entry: b)).ToDictionary(x => x.Key, x => x.Entry);
        CharactersByCode = ctx.Bible
            .Where(b => b.Category == "Character")
            .ToDictionary(b => b.Code.ToUpperInvariant(), b => b);
    }

    public static Dictionary<string, Guid> LineKeys(ScriptDraft draft) =>
        draft.Lines.ToDictionary(l => $"L{l.Order}", l => l.LineId);

    public string? CodeOf(Guid? speakerId) =>
        speakerId is null ? null : CharactersByCode.Values.FirstOrDefault(c => c.EntryId == speakerId)?.Code;
}

/// <summary>Dựng các khối văn bản đưa vào prompt (định dạng cố định: Mock fixture cũng đọc lại định dạng này).</summary>
public static class PromptText
{
    public static string BibleBlock(AgentContext ctx, KeyMaps keys)
    {
        var sb = new StringBuilder();
        foreach (var (key, b) in keys.Bible)
            sb.AppendLine($"[{key}] code={b.Code} | category={b.Category} | name={b.Name}")
              .AppendLine($"description: {Flat(b.Description)}");
        return sb.ToString().TrimEnd();
    }

    public static string ScriptBlock(ScriptDraft draft, KeyMaps keys)
    {
        var sb = new StringBuilder();
        foreach (var l in draft.Lines.OrderBy(l => l.Order))
            sb.AppendLine($"[L{l.Order}] speaker={keys.CodeOf(l.SpeakerId) ?? "-"} | dialogue=\"{Flat(l.Dialogue)}\" | stage=\"{Flat(l.StageDirection)}\"");
        return sb.ToString().TrimEnd();
    }

    /// <summary>Script có sẵn của Scene coi như bản nháp lượt 0 — đầu vào của Continuity/Storyboard/Editor khi workflow không có Writer.</summary>
    public static ScriptDraft BaselineDraft(AgentContext ctx) =>
        new(AiProtocol.SchemaVersion, ctx.RunId, ctx.SceneId, 0,
            ctx.BaselineScript.OrderBy(l => l.Order)
                .Select(l => new ScriptDraftLine(l.LineId, l.Order, l.SpeakerId, l.Dialogue, l.StageDirection)).ToList(),
            ctx.Bible.Select(b => b.RevisionId).ToList(), null);

    public static string BaselineBlock(AgentContext ctx, KeyMaps keys)
    {
        if (ctx.BaselineScript.Count == 0) return "(Scene chưa có script)";
        var sb = new StringBuilder();
        foreach (var l in ctx.BaselineScript.OrderBy(l => l.Order))
            sb.AppendLine($"[L{l.Order}] speaker={keys.CodeOf(l.SpeakerId) ?? "-"} | dialogue=\"{Flat(l.Dialogue)}\" | stage=\"{Flat(l.StageDirection)}\"");
        return sb.ToString().TrimEnd();
    }

    private static string Flat(string? s) => (s ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
}