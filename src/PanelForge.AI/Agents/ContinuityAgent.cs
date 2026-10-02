using System.Text;
using PanelForge.AI.Contracts;
using PanelForge.AI.Validators;

namespace PanelForge.AI.Agents;

/// <summary>Đối chiếu script với Bible snapshot; mỗi finding phải có script-line + trích dẫn nguyên văn từ Bible revision.</summary>
public sealed class ContinuityAgent
{
    public static readonly PromptInfo Prompt = PromptLibrary.Load("continuity");

    private readonly AgentRunner _runner;

    public ContinuityAgent(AgentRunner runner) => _runner = runner;

    public async Task<(AgentCall<ContinuityReport> Call, string InputJson)> RunAsync(
        AgentContext ctx, KeyMaps keys, ScriptDraft draft, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[[agent:continuity]] [[round:{draft.Round}]]");
        sb.AppendLine($"## Bible hiệu lực tại chương {ctx.ChapterNumber} (dữ liệu)").AppendLine(PromptText.BibleBlock(ctx, keys)).AppendLine();
        sb.AppendLine("## Script cần kiểm tra (dữ liệu)").AppendLine(PromptText.ScriptBlock(draft, keys)).AppendLine();
        sb.AppendLine("Hãy trả về JSON theo đúng định dạng đã nêu.");

        var inputJson = AiJson.Serialize(new
        {
            round = draft.Round,
            chapterNumber = ctx.ChapterNumber,
            bible = keys.Bible.Select(kv => new { key = kv.Key, entryId = kv.Value.EntryId, revisionId = kv.Value.RevisionId, code = kv.Value.Code }),
            scriptLineIds = draft.Lines.Select(l => new { key = $"L{l.Order}", lineId = l.LineId })
        });

        var bibleByRevision = ctx.Bible.ToDictionary(b => b.RevisionId, b => (b.EntryId, $"{b.Name}\n{b.Description}"));
        var lineKeys = KeyMaps.LineKeys(draft);
        var call = await _runner.RunAsync<ContinuityReport>(Prompt, sb.ToString(),
            text => Parse(text, ctx, keys, draft, lineKeys, bibleByRevision), ct);
        return (call, inputJson);
    }

    private static (ContinuityReport?, IReadOnlyList<string>) Parse(
        string text, AgentContext ctx, KeyMaps keys, ScriptDraft draft, Dictionary<string, Guid> lineKeys,
        IReadOnlyDictionary<Guid, (Guid EntryId, string Text)> bibleByRevision)
    {
        var model = OutputParser.Parse<ContinuityOutput>(text, out var error);
        if (model is null) return (null, [error!]);

        var errors = new List<string>();
        var findings = new List<ContinuityFinding>();
        var idx = 0;
        foreach (var f in model.Findings ?? [])
        {
            idx++;
            var ids = new List<Guid>();
            foreach (var key in f.LineKeys ?? [])
            {
                if (lineKeys.TryGetValue(key.Trim().ToUpperInvariant(), out var id)) ids.Add(id);
                else errors.Add($"Finding {idx}: lineKey '{key}' không có trong script.");
            }
            var evidence = new List<BibleEvidence>();
            foreach (var e in f.Evidence ?? [])
            {
                if (e.BibleKey is not null && keys.Bible.TryGetValue(e.BibleKey.Trim().ToUpperInvariant(), out var b))
                    evidence.Add(new BibleEvidence(b.EntryId, b.RevisionId, e.Quote?.Trim() ?? ""));
                else errors.Add($"Finding {idx}: bibleKey '{e.BibleKey}' không có trong Bible.");
            }
            findings.Add(new ContinuityFinding($"F{draft.Round}-{idx}", f.Severity?.Trim() ?? "", f.Category?.Trim() ?? "Other",
                f.Description?.Trim() ?? "", ids, evidence, f.SuggestedFix?.Trim()));
        }
        if (errors.Count > 0) return (null, errors);

        var report = new ContinuityReport(AiProtocol.SchemaVersion, ctx.RunId, draft.Round, findings, model.Summary?.Trim() ?? "");
        var validation = ContentRules.ValidateContinuity(report, ctx.RunId, draft, bibleByRevision);
        return validation.Count == 0 ? (report, []) : (null, validation);
    }
}