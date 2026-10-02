using System.Text;
using PanelForge.AI.Contracts;
using PanelForge.AI.Validators;

namespace PanelForge.AI.Agents;

/// <summary>Chia script thành Page/Panel (bbox, góc máy, thứ tự đọc). Không tạo lettering. Số panel/thứ tự đọc do code gán.</summary>
public sealed class StoryboardAgent
{
    public static readonly PromptInfo Prompt = PromptLibrary.Load("storyboard");

    private readonly AgentRunner _runner;

    public StoryboardAgent(AgentRunner runner) => _runner = runner;

    public async Task<(AgentCall<StoryboardLayout> Call, string InputJson)> RunAsync(
        AgentContext ctx, KeyMaps keys, ScriptDraft script, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[[agent:storyboard]] [[direction:{ctx.ReadingDirection}]]");
        sb.AppendLine($"## Hướng đọc: {ctx.ReadingDirection}").AppendLine();
        sb.AppendLine("## Nhân vật (code)").AppendLine(string.Join(", ", keys.CharactersByCode.Keys)).AppendLine();
        sb.AppendLine("## Script cuối đã qua Continuity (dữ liệu)").AppendLine(PromptText.ScriptBlock(script, keys)).AppendLine();
        sb.AppendLine("Hãy trả về JSON theo đúng định dạng đã nêu.");

        var inputJson = AiJson.Serialize(new
        {
            readingDirection = ctx.ReadingDirection,
            scriptRound = script.Round,
            scriptLineIds = script.Lines.Select(l => new { key = $"L{l.Order}", lineId = l.LineId })
        });

        var lineKeys = KeyMaps.LineKeys(script);
        var call = await _runner.RunAsync<StoryboardLayout>(Prompt, sb.ToString(),
            text => Parse(text, ctx, keys, script, lineKeys), ct);
        return (call, inputJson);
    }

    private static (StoryboardLayout?, IReadOnlyList<string>) Parse(
        string text, AgentContext ctx, KeyMaps keys, ScriptDraft script, Dictionary<string, Guid> lineKeys)
    {
        // Demo: Storyboard không được tự tạo lettering elements
        if (OutputParser.HasProperty(text, "elements"))
            return (null, ["Storyboard không được tạo lettering (không có trường 'elements', bubble hay SFX)."]);

        var model = OutputParser.Parse<StoryboardOutput>(text, out var error);
        if (model is null) return (null, [error!]);
        if (model.Pages is null or { Count: 0 }) return (null, ["Thiếu mảng pages."]);

        var errors = new List<string>();
        var pages = new List<LayoutPage>();
        var order = 0;
        for (var p = 0; p < model.Pages.Count; p++)
        {
            var panels = new List<LayoutPanel>();
            var src = model.Pages[p].Panels ?? [];
            for (var n = 0; n < src.Count; n++)
            {
                var pn = src[n];
                var ids = new List<Guid>();
                foreach (var key in pn.LineKeys ?? [])
                {
                    if (lineKeys.TryGetValue(key.Trim().ToUpperInvariant(), out var id)) ids.Add(id);
                    else errors.Add($"Panel {p + 1}.{n + 1}: lineKey '{key}' không có trong script.");
                }
                var chars = new List<Guid>();
                foreach (var code in pn.Characters ?? [])
                {
                    if (keys.CharactersByCode.TryGetValue(code.Trim().ToUpperInvariant(), out var c)) chars.Add(c.EntryId);
                    else errors.Add($"Panel {p + 1}.{n + 1}: nhân vật '{code}' không có trong Bible.");
                }
                if (pn.Bbox is null) { errors.Add($"Panel {p + 1}.{n + 1}: thiếu bbox."); continue; }

                // panelNumber và readingOrder do code gán theo đúng thứ tự model liệt kê
                panels.Add(new LayoutPanel(n + 1, ++order, pn.Bbox, AiProtocol.PagePixelSpace,
                    pn.Camera?.Trim() ?? "", pn.Description?.Trim() ?? "", chars, ids));
            }
            pages.Add(new LayoutPage(p + 1, panels));
        }
        if (errors.Count > 0) return (null, errors);

        var layout = new StoryboardLayout(AiProtocol.SchemaVersion, ctx.RunId, ctx.SceneId, ctx.ReadingDirection, pages, model.Notes);
        var validation = ContentRules.ValidateLayout(
            layout, ctx.RunId, ctx.SceneId, script, ctx.ReadingDirection,
            keys.CharactersByCode.Values.Select(c => c.EntryId).ToHashSet());
        return validation.Count == 0 ? (layout, []) : (null, validation);
    }
}