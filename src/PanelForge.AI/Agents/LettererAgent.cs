using System.Text;
using PanelForge.AI.Contracts;
using PanelForge.AI.Validators;

namespace PanelForge.AI.Agents;

/// <summary>
/// Đề xuất rút gọn thoại cho vừa bubble. Liên kết dòng ↔ panel ↔ bubble, văn bản gốc và giới hạn chữ do CODE cung cấp và kiểm
/// (single-binding, ngắn hơn bản gốc, trong giới hạn ƯỚC LƯỢNG). Model chỉ chọn lineKey + viết suggestedText + lý do;
/// không có kết luận "chắc chắn vừa bubble" nào của LLM được lưu.
/// </summary>
public sealed class LettererAgent
{
    public static readonly PromptInfo Prompt = PromptLibrary.Load("letterer");

    private readonly AgentRunner _runner;

    public LettererAgent(AgentRunner runner) => _runner = runner;

    public async Task<(AgentCall<LetteringSuggestionSet> Call, string InputJson)> RunAsync(
        AgentContext ctx, KeyMaps keys, CancellationToken ct)
    {
        var lettering = ctx.Lettering ?? throw new InvalidOperationException("Run Letterer thiếu context thoại.");
        var lines = lettering.Lines.OrderBy(l => l.Order).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"[[agent:letterer]] [[lines:{lines.Count}]]");
        sb.AppendLine("## Quy ước giới hạn (code)")
          .AppendLine("`limit` là số ký tự tối đa ƯỚC LƯỢNG từ kích thước bubble (element-estimate) hoặc diện tích panel (panel-estimate); không phải kết quả render.")
          .AppendLine();
        sb.AppendLine($"## Bible hiệu lực tại chương {ctx.ChapterNumber} (dữ liệu)").AppendLine(PromptText.BibleBlock(ctx, keys)).AppendLine();
        sb.AppendLine("## Dòng thoại cần xem xét (dữ liệu)");
        foreach (var l in lines)
        {
            var bubble = l.ElementId is null ? "-" : $"{l.ElementType ?? "bubble"}";
            sb.AppendLine($"[L{l.Order}] speaker={keys.CodeOf(l.SpeakerId) ?? "-"} | page={l.PageNumber} panel={l.PanelNumber} | bubble={bubble} | limit={l.MaxChars} ({l.LimitSource}) | length={l.Dialogue.Length}{(l.Dialogue.Length > l.MaxChars ? " QUÁ GIỚI HẠN" : "")} | dialogue=\"{Flat(l.Dialogue)}\"");
        }
        sb.AppendLine().AppendLine("Hãy trả về JSON theo đúng định dạng đã nêu.");

        var inputJson = AiJson.Serialize(new
        {
            layoutRevisionId = lettering.LayoutRevisionId,
            lines = lines.Select(l => new
            {
                key = $"L{l.Order}", lineId = l.LineId, panelId = l.PanelId, elementId = l.ElementId,
                maxChars = l.MaxChars, limitSource = l.LimitSource, length = l.Dialogue.Length
            })
        });

        var byKey = lines.ToDictionary(l => $"L{l.Order}");
        var call = await _runner.RunAsync<LetteringSuggestionSet>(Prompt, sb.ToString(),
            text => Parse(text, ctx, lettering, byKey), ct);
        return (call, inputJson);
    }

    private static (LetteringSuggestionSet?, IReadOnlyList<string>) Parse(
        string text, AgentContext ctx, LetteringContext lettering, Dictionary<string, LetteringLineContext> byKey)
    {
        var model = OutputParser.Parse<LettererOutput>(text, out var error);
        if (model is null) return (null, [error!]);

        var errors = new List<string>();
        var result = new List<LetteringSuggestion>();
        foreach (var s in model.Suggestions ?? [])
        {
            if (s.LineKey is null || !byKey.TryGetValue(s.LineKey.Trim().ToUpperInvariant(), out var line))
            {
                errors.Add($"lineKey '{s.LineKey}' không có trong danh sách thoại.");
                continue;
            }
            var suggested = s.SuggestedText?.Trim() ?? "";
            // Liên kết, văn bản gốc, giới hạn lấy từ context của code — không lấy từ model
            result.Add(new LetteringSuggestion(line.LineId, line.ElementId, line.PanelId, line.Dialogue, suggested,
                s.Reason?.Trim() ?? "", line.MaxChars, line.LimitSource, suggested.Length > 0 && suggested.Length <= line.MaxChars));
        }
        if (errors.Count > 0) return (null, errors);

        var set = new LetteringSuggestionSet(AiProtocol.SchemaVersion, ctx.RunId, ctx.SceneId, result, model.Summary?.Trim());
        var validation = ContentRules.ValidateLettering(set, ctx.RunId, ctx.SceneId, lettering);
        return validation.Count == 0 ? (set, []) : (null, validation);
    }

    private static string Flat(string? s) => (s ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
}
