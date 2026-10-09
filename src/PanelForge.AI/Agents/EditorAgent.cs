using System.Text;
using PanelForge.AI.Contracts;
using PanelForge.AI.Validators;

namespace PanelForge.AI.Agents;

/// <summary>
/// Viết báo cáo review từ script cuối + finding + layout. Checklist do CODE gom (không giao cho LLM) rồi đưa cho agent diễn giải.
/// Chỉ đề xuất: không áp dụng nội dung, không chuyển stage, không Approve Chapter.
/// </summary>
public sealed class EditorAgent
{
    public static readonly PromptInfo Prompt = PromptLibrary.Load("editor");

    private readonly AgentRunner _runner;

    public EditorAgent(AgentRunner runner) => _runner = runner;

    /// <param name="layout">Null khi review script chưa có layout (workflow Editor trên Scene chưa Storyboard): checklist ghi rõ là chưa có.</param>
    public static IReadOnlyList<ChecklistItem> BuildChecklist(
        ScriptDraft script, ContinuityReport finalContinuity, StoryboardLayout? layout, string direction)
    {
        if (layout is null)
        {
            var b = finalContinuity.Findings.Count(f => f.Severity == AiProtocol.SeverityBlocking);
            return
            [
                new("Không còn finding chặn của Continuity", b == 0, $"{b} finding Blocking"),
                new("Có layout đã duyệt để review", false, "Scene chưa có layout đã Accept: chỉ review được script"),
                new("Finding không chặn còn lại", finalContinuity.Findings.Count - b == 0, $"{finalContinuity.Findings.Count - b} finding Warning/Info")
            ];
        }
        var covered = layout.Pages.SelectMany(p => p.Panels).SelectMany(p => p.SourceScriptLineIds).ToHashSet();
        var uncovered = script.Lines.Count(l => !covered.Contains(l.LineId));
        var bboxErrors = layout.Pages.SelectMany(pg => pg.Panels.SelectMany(pn =>
            ContentRules.ValidateBbox(pn.Bbox, $"panel {pg.PageNumber}.{pn.PanelNumber}"))).Count();
        var orderErrors = ContentRules.ValidateReadingOrder(layout, direction).Count;
        var blocking = finalContinuity.Findings.Count(f => f.Severity == AiProtocol.SeverityBlocking);
        var remaining = finalContinuity.Findings.Count - blocking;

        return
        [
            new("Không còn finding chặn của Continuity", blocking == 0, $"{blocking} finding Blocking"),
            new("Mọi dòng script được panel tham chiếu", uncovered == 0, $"{uncovered}/{script.Lines.Count} dòng chưa được tham chiếu"),
            new("Bbox hợp lệ (page-normalized)", bboxErrors == 0, $"{bboxErrors} lỗi bbox"),
            new($"Thứ tự đọc hợp lệ ({direction})", orderErrors == 0, $"{orderErrors} lỗi thứ tự đọc"),
            new("Số lượt sửa của Writer", true, $"{script.Round} lượt"),
            new("Finding không chặn còn lại", remaining == 0, $"{remaining} finding Warning/Info")
        ];
    }

    public async Task<(AgentCall<ReviewReport> Call, string InputJson)> RunAsync(
        AgentContext ctx, KeyMaps keys, ScriptDraft script, ContinuityReport finalContinuity, StoryboardLayout? layout,
        CancellationToken ct)
    {
        var checklist = BuildChecklist(script, finalContinuity, layout, ctx.ReadingDirection);
        var pageCount = layout?.Pages.Count ?? 0;
        var panelCount = layout?.Pages.Sum(p => p.Panels.Count) ?? 0;
        var lineKeysById = script.Lines.ToDictionary(l => l.LineId, l => $"L{l.Order}");

        var sb = new StringBuilder();
        sb.AppendLine($"[[agent:editor]] [[final-round:{script.Round}]] [[lines:{script.Lines.Count}]] [[panels:{panelCount}]] [[pages:{pageCount}]] [[warnings:{finalContinuity.Findings.Count}]]");
        sb.AppendLine("## Script cuối (dữ liệu)").AppendLine(PromptText.ScriptBlock(script, keys)).AppendLine();
        sb.AppendLine("## Finding của lần Continuity cuối (dữ liệu)");
        foreach (var f in finalContinuity.Findings)
            sb.AppendLine($"- {f.Severity} dòng [{string.Join(",", f.ScriptLineIds.Select(i => lineKeysById[i]))}]: {f.Description}");
        if (finalContinuity.Findings.Count == 0) sb.AppendLine("(không có)");
        sb.AppendLine().AppendLine("## Layout Storyboard (dữ liệu)");
        if (layout is null) sb.AppendLine("(Scene chưa có layout đã duyệt)");
        foreach (var pg in layout?.Pages ?? [])
            foreach (var pn in pg.Panels)
                sb.AppendLine($"Panel:{pg.PageNumber}.{pn.PanelNumber} đọc#{pn.ReadingOrder} camera={pn.Camera} dòng=[{string.Join(",", pn.SourceScriptLineIds.Where(lineKeysById.ContainsKey).Select(i => lineKeysById[i]))}] — {pn.Description}");
        sb.AppendLine().AppendLine("## Checklist do hệ thống tính (sự thật, không phản bác)");
        foreach (var c in checklist) sb.AppendLine($"- [{(c.Passed ? "đạt" : "KHÔNG đạt")}] {c.Name}: {c.Detail}");
        sb.AppendLine().AppendLine("Hãy trả về JSON theo đúng định dạng đã nêu.");

        var inputJson = AiJson.Serialize(new
        {
            scriptRound = script.Round,
            continuityRound = finalContinuity.Round,
            findingCount = finalContinuity.Findings.Count,
            pages = pageCount,
            panels = panelCount,
            hasLayout = layout is not null,
            checklist
        });

        var lineKeys = KeyMaps.LineKeys(script);
        var call = await _runner.RunAsync<ReviewReport>(Prompt, sb.ToString(),
            text => Parse(text, ctx, script, lineKeys, checklist), ct);
        return (call, inputJson);
    }

    private static (ReviewReport?, IReadOnlyList<string>) Parse(
        string text, AgentContext ctx, ScriptDraft script, Dictionary<string, Guid> lineKeys, IReadOnlyList<ChecklistItem> checklist)
    {
        var model = OutputParser.Parse<EditorOutput>(text, out var error);
        if (model is null) return (null, [error!]);

        var errors = new List<string>();
        var items = new List<ReviewItem>();
        foreach (var i in model.Items ?? [])
        {
            var ids = new List<Guid>();
            foreach (var key in i.LineKeys ?? [])
            {
                if (lineKeys.TryGetValue(key.Trim().ToUpperInvariant(), out var id)) ids.Add(id);
                else errors.Add($"Mục '{i.Target}': lineKey '{key}' không có trong script.");
            }
            items.Add(new ReviewItem(i.Target?.Trim() ?? "Scene", i.Severity?.Trim() ?? "Info", i.Comment?.Trim() ?? "", ids));
        }
        if (errors.Count > 0) return (null, errors);

        var report = new ReviewReport(AiProtocol.SchemaVersion, ctx.RunId, model.Verdict?.Trim() ?? "",
            model.Summary?.Trim() ?? "", checklist, items, model.DraftRejectComment);
        var validation = ContentRules.ValidateReview(report, ctx.RunId, script);
        return validation.Count == 0 ? (report, []) : (null, validation);
    }
}