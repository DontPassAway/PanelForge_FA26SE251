using System.Text;
using PanelForge.AI.Contracts;
using PanelForge.AI.Validators;

namespace PanelForge.AI.Agents;

/// <summary>
/// Viết báo cáo tiến độ từ snapshot do Backend tính. Mọi con số/phần trăm thuộc về code (snapshot); agent chỉ diễn giải và phải trích
/// khoá fact làm nguồn cho từng rủi ro/đề xuất. Báo cáo chỉ để tham khảo: agent không giao việc, không chuyển stage, không Approve.
/// </summary>
public sealed class ProducerAgent
{
    public static readonly PromptInfo Prompt = PromptLibrary.Load("producer");

    private readonly AgentRunner _runner;

    public ProducerAgent(AgentRunner runner) => _runner = runner;

    public async Task<(AgentCall<ProductionReport> Call, string InputJson)> RunAsync(AgentContext ctx, CancellationToken ct)
    {
        var progress = ctx.Progress ?? throw new InvalidOperationException("Run Producer thiếu snapshot tiến độ.");

        var sb = new StringBuilder();
        sb.AppendLine($"[[agent:producer]] [[facts:{progress.Facts.Count}]] [[gaps:{progress.DataGaps.Count}]]");
        sb.AppendLine("## Mục tiêu của người dùng (brief — dữ liệu)").AppendLine(ctx.Brief).AppendLine();
        sb.AppendLine($"## Phạm vi: {progress.ScopeLabel}; số liệu tính lúc {progress.AsOfUtc:yyyy-MM-dd HH:mm} UTC").AppendLine();
        sb.AppendLine("## Fact do hệ thống tính (sự thật, không phản bác)");
        foreach (var f in progress.Facts) sb.AppendLine($"[{f.Key}] {f.Text}");
        sb.AppendLine().AppendLine("## Dữ liệu còn thiếu (do hệ thống xác định)");
        if (progress.DataGaps.Count == 0) sb.AppendLine("(không có)");
        foreach (var g in progress.DataGaps) sb.AppendLine($"- {g}");
        sb.AppendLine().AppendLine("## Số phần trăm được phép nhắc");
        sb.AppendLine(progress.AllowedPercentages.Count == 0 ? "(không có)" : string.Join(", ", progress.AllowedPercentages));
        sb.AppendLine().AppendLine("Hãy trả về JSON theo đúng định dạng đã nêu.");

        var inputJson = AiJson.Serialize(new
        {
            scope = progress.ScopeLabel, asOfUtc = progress.AsOfUtc, facts = progress.Facts,
            dataGaps = progress.DataGaps, allowedPercentages = progress.AllowedPercentages
        });

        var call = await _runner.RunAsync<ProductionReport>(Prompt, sb.ToString(), text => Parse(text, ctx, progress), ct);
        return (call, inputJson);
    }

    private static (ProductionReport?, IReadOnlyList<string>) Parse(string text, AgentContext ctx, ProgressContext progress)
    {
        var model = OutputParser.Parse<ProducerOutput>(text, out var error);
        if (model is null) return (null, [error!]);

        static List<string> Keys(List<string>? raw) => (raw ?? []).Select(k => k.Trim().ToUpperInvariant()).Distinct().ToList();
        var risks = (model.Risks ?? []).Select(r =>
            new ProductionRisk(r.Severity?.Trim() ?? "", r.Description?.Trim() ?? "", Keys(r.SourceFactKeys))).ToList();
        var recs = (model.Recommendations ?? []).Select(r =>
            new ProductionRecommendation(r.Action?.Trim() ?? "", r.Rationale?.Trim() ?? "", Keys(r.SourceFactKeys),
                string.IsNullOrWhiteSpace(r.SuggestedOwnerRole) ? null : r.SuggestedOwnerRole.Trim())).ToList();

        // Facts và DataGaps do code sao chép từ snapshot (model không được sửa)
        var report = new ProductionReport(AiProtocol.SchemaVersion, ctx.RunId, model.Headline?.Trim() ?? "",
            model.Summary?.Trim() ?? "", risks, recs, progress.DataGaps, progress.Facts);
        var validation = ContentRules.ValidateProduction(report, ctx.RunId, progress);
        return validation.Count == 0 ? (report, []) : (null, validation);
    }
}
