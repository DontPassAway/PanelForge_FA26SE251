using System.Text;
using PanelForge.AI.Contracts;
using PanelForge.AI.Validators;

namespace PanelForge.AI.Agents;

/// <summary>Viết script cho một Scene từ brief + Bible snapshot; ở lượt sửa nhận bản nháp trước + finding chặn của Continuity.</summary>
public sealed class WriterAgent
{
    public static readonly PromptInfo Prompt = PromptLibrary.Load("writer");

    private readonly AgentRunner _runner;

    public WriterAgent(AgentRunner runner) => _runner = runner;

    public async Task<(AgentCall<ScriptDraft> Call, string InputJson)> RunAsync(
        AgentContext ctx, KeyMaps keys, int round, ScriptDraft? previous, ContinuityReport? previousFindings, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[[agent:writer]] [[round:{round}]] [[direction:{ctx.ReadingDirection}]]");
        sb.AppendLine("## Brief (dữ liệu)").AppendLine(ctx.Brief).AppendLine();
        sb.AppendLine("## Scene").AppendLine($"{ctx.SceneHeading} — {ctx.SceneSummary}").AppendLine();
        if (!string.IsNullOrWhiteSpace(ctx.SeriesStoryArc))
            sb.AppendLine("## Story arc của Series (dữ liệu)").AppendLine(ctx.SeriesStoryArc).AppendLine();
        if (!string.IsNullOrWhiteSpace(ctx.ChapterOutline))
            sb.AppendLine($"## Outline chương {ctx.ChapterNumber} (dữ liệu)").AppendLine(ctx.ChapterOutline).AppendLine();
        sb.AppendLine($"## Bible hiệu lực tại chương {ctx.ChapterNumber} (dữ liệu)").AppendLine(PromptText.BibleBlock(ctx, keys)).AppendLine();
        sb.AppendLine("## Script có sẵn trong scene (dữ liệu)").AppendLine(PromptText.BaselineBlock(ctx, keys)).AppendLine();

        object? findingsInput = null;
        if (previous is not null && previousFindings is not null)
        {
            var blocking = previousFindings.Findings.Where(f => f.Severity == AiProtocol.SeverityBlocking).ToList();
            sb.AppendLine($"## Bản nháp trước (lượt {previous.Round})").AppendLine(PromptText.ScriptBlock(previous, keys)).AppendLine();
            sb.AppendLine("## Finding Blocking của Continuity cần xử lý");
            var lineKeysById = previous.Lines.ToDictionary(l => l.LineId, l => $"L{l.Order}");
            foreach (var f in blocking)
                sb.AppendLine($"- dòng [{string.Join(",", f.ScriptLineIds.Select(i => lineKeysById[i]))}]: {f.Description} | gợi ý: {f.SuggestedFix}");
            findingsInput = blocking;
        }
        sb.AppendLine().AppendLine("Hãy trả về JSON theo đúng định dạng đã nêu.");

        var inputJson = AiJson.Serialize(new
        {
            round,
            brief = ctx.Brief,
            readingDirection = ctx.ReadingDirection,
            bible = keys.Bible.Select(kv => new { key = kv.Key, entryId = kv.Value.EntryId, revisionId = kv.Value.RevisionId, code = kv.Value.Code }),
            baselineScript = ctx.BaselineScript,
            previousDraft = previous,
            blockingFindings = findingsInput
        });

        var call = await _runner.RunAsync<ScriptDraft>(Prompt, sb.ToString(), text => Parse(text, ctx, keys, round), ct);
        return (call, inputJson);
    }

    private static (ScriptDraft?, IReadOnlyList<string>) Parse(string text, AgentContext ctx, KeyMaps keys, int round)
    {
        var model = OutputParser.Parse<WriterOutput>(text, out var error);
        if (model is null) return (null, [error!]);
        if (model.Lines is null or { Count: 0 }) return (null, ["Thiếu mảng lines."]);

        var errors = new List<string>();
        var lines = new List<ScriptDraftLine>();
        for (var i = 0; i < model.Lines.Count; i++)
        {
            var l = model.Lines[i];
            Guid? speaker = null;
            if (!string.IsNullOrWhiteSpace(l.Speaker) && !l.Speaker.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                if (keys.CharactersByCode.TryGetValue(l.Speaker.Trim().ToUpperInvariant(), out var c)) speaker = c.EntryId;
                else errors.Add($"Dòng {i + 1}: speaker '{l.Speaker}' không phải code nhân vật trong Bible.");
            }
            // LineId do ứng dụng cấp, không phải model
            lines.Add(new ScriptDraftLine(Guid.NewGuid(), i + 1, speaker,
                string.IsNullOrWhiteSpace(l.Dialogue) ? null : l.Dialogue.Trim(),
                string.IsNullOrWhiteSpace(l.StageDirection) ? null : l.StageDirection.Trim()));
        }
        if (errors.Count > 0) return (null, errors);

        var draft = new ScriptDraft(AiProtocol.SchemaVersion, ctx.RunId, ctx.SceneId, round, lines,
            ctx.BibleRevisionIds, model.RevisionNotes);
        var validation = ContentRules.ValidateScript(
            draft, ctx.RunId, ctx.SceneId,
            keys.CharactersByCode.Values.Select(c => c.EntryId).ToHashSet(), ctx.BibleRevisionIds.ToHashSet());
        return validation.Count == 0 ? (draft, []) : (null, validation);
    }
}