using System.Text;
using PanelForge.AI.Contracts;
using PanelForge.AI.Validators;

namespace PanelForge.AI.Agents;

/// <summary>
/// Soạn prompt ảnh cho từng panel của layout đã duyệt. Chỉ soạn văn bản: không sinh ảnh, không sửa Bible/layout/script.
/// panelId, số trang/panel và referenceIds do CODE điền/kiểm từ context (model chỉ chọn khoá B# có trong danh sách Bible).
/// </summary>
public sealed class ArtistAgent
{
    public static readonly PromptInfo Prompt = PromptLibrary.Load("artist");

    private readonly AgentRunner _runner;

    public ArtistAgent(AgentRunner runner) => _runner = runner;

    public async Task<(AgentCall<ArtistPromptSet> Call, string InputJson)> RunAsync(
        AgentContext ctx, KeyMaps keys, CancellationToken ct)
    {
        var artist = ctx.Artist ?? throw new InvalidOperationException("Run Artist thiếu context panel.");
        var panels = artist.Panels.OrderBy(p => p.ReadingOrder).ToList();
        var panelKeys = panels.Select((p, i) => (Key: $"P{i + 1}", Panel: p)).ToList();
        var lineText = ctx.BaselineScript.ToDictionary(l => l.LineId);
        var codeById = keys.CharactersByCode.Values.ToDictionary(c => c.EntryId, c => c.Code);

        var sb = new StringBuilder();
        sb.AppendLine($"[[agent:artist]] [[panels:{panels.Count}]] [[direction:{ctx.ReadingDirection}]]");
        sb.AppendLine($"## Style rules của Series (dữ liệu)");
        if (artist.StyleRules.Count == 0) sb.AppendLine("(Series chưa có style rule nào)");
        foreach (var r in artist.StyleRules)
            sb.AppendLine($"- {r.Name} [{r.RuleType}/{r.Severity}]: {Flat(r.Description)}");
        sb.AppendLine().AppendLine($"## Bible hiệu lực tại chương {ctx.ChapterNumber} (dữ liệu)").AppendLine(PromptText.BibleBlock(ctx, keys)).AppendLine();
        sb.AppendLine("## Panel cần soạn prompt (dữ liệu, theo thứ tự đọc)");
        foreach (var (key, p) in panelKeys)
        {
            var chars = p.CharacterIds.Select(id => codeById.TryGetValue(id, out var code) ? code : "?").ToList();
            sb.AppendLine($"[{key}] page={p.PageNumber} panel={p.PanelNumber} order={p.ReadingOrder} camera={Flat(p.Camera)} | characters={(chars.Count == 0 ? "-" : string.Join(",", chars))}");
            sb.AppendLine($"description: {Flat(p.Description)}");
            foreach (var id in p.SourceScriptLineIds)
                if (lineText.TryGetValue(id, out var l))
                    sb.AppendLine($"  line L{l.Order} speaker={keys.CodeOf(l.SpeakerId) ?? "-"}: {Flat(l.Dialogue)} {Flat(l.StageDirection)}".TrimEnd());
        }
        sb.AppendLine().AppendLine("Hãy trả về JSON theo đúng định dạng đã nêu.");

        var inputJson = AiJson.Serialize(new
        {
            layoutRevisionId = artist.LayoutRevisionId,
            panels = panelKeys.Select(x => new { key = x.Key, panelId = x.Panel.PanelId, page = x.Panel.PageNumber, panel = x.Panel.PanelNumber, characterIds = x.Panel.CharacterIds }),
            styleRules = artist.StyleRules,
            references = keys.Bible.Select(kv => new { key = kv.Key, entryId = kv.Value.EntryId, revisionId = kv.Value.RevisionId, code = kv.Value.Code })
        });

        var call = await _runner.RunAsync<ArtistPromptSet>(Prompt, sb.ToString(),
            text => Parse(text, ctx, keys, artist, panelKeys.ToDictionary(x => x.Key, x => x.Panel)), ct);
        return (call, inputJson);
    }

    private static (ArtistPromptSet?, IReadOnlyList<string>) Parse(
        string text, AgentContext ctx, KeyMaps keys, ArtistContext artist, Dictionary<string, ArtistPanelContext> panelsByKey)
    {
        var model = OutputParser.Parse<ArtistOutput>(text, out var error);
        if (model is null) return (null, [error!]);
        if (model.Panels is null or { Count: 0 }) return (null, ["Thiếu mảng panels."]);

        var errors = new List<string>();
        var result = new List<ArtistPanelPrompt>();
        foreach (var p in model.Panels)
        {
            if (p.PanelKey is null || !panelsByKey.TryGetValue(p.PanelKey.Trim().ToUpperInvariant(), out var panel))
            {
                errors.Add($"panelKey '{p.PanelKey}' không có trong danh sách panel.");
                continue;
            }
            var refs = new List<Guid>();
            foreach (var key in p.ReferenceKeys ?? [])
            {
                if (keys.Bible.TryGetValue(key.Trim().ToUpperInvariant(), out var b))
                {
                    if (!refs.Contains(b.EntryId)) refs.Add(b.EntryId);
                }
                else errors.Add($"Panel {panel.PageNumber}.{panel.PanelNumber}: referenceKey '{key}' không có trong Bible.");
            }
            result.Add(new ArtistPanelPrompt(panel.PanelId, panel.PageNumber, panel.PanelNumber,
                p.Prompt?.Trim() ?? "", refs, string.IsNullOrWhiteSpace(p.ConsistencyNotes) ? null : p.ConsistencyNotes.Trim()));
        }
        if (errors.Count > 0) return (null, errors);

        var set = new ArtistPromptSet(AiProtocol.SchemaVersion, ctx.RunId, ctx.SceneId, artist.LayoutRevisionId,
            result.OrderBy(r => panelsByKey.Values.First(v => v.PanelId == r.PanelId).ReadingOrder).ToList(), model.StyleSummary?.Trim());
        var validation = ContentRules.ValidateArtist(set, ctx.RunId, ctx.SceneId, artist, ctx.Bible.Select(b => b.EntryId).ToHashSet());
        return validation.Count == 0 ? (set, []) : (null, validation);
    }

    private static string Flat(string? s) => (s ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
}
