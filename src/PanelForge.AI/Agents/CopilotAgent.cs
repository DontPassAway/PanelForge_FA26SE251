using System.Text;
using PanelForge.AI.Contracts;
using PanelForge.AI.Validators;

namespace PanelForge.AI.Agents;

public sealed record CopilotOutput(string? Text, string? Reason);
public sealed record MetadataOutput(string? Logline, string? Synopsis, string? Genre, string? Teaser, string? AgeRating, string? Reason);

/// <summary>
/// Copilot sync: một lần gọi model cho MỘT dòng thoại (viết lại giọng / rút gọn theo maxChars do FE đo).
/// Dùng chung AgentRunner (parse JSON + đúng một lần sửa output); kiểm tra kết quả bằng <see cref="CopilotRules"/>.
/// </summary>
public sealed class CopilotAgent
{
    private static readonly Dictionary<string, PromptInfo> Prompts = new()
    {
        [CopilotTasks.WriterRewrite] = PromptLibrary.Load("copilot_writer_rewrite"),
        [CopilotTasks.LettererShorten] = PromptLibrary.Load("copilot_letterer_shorten"),
        [CopilotTasks.PublisherMetadata] = PromptLibrary.Load("copilot_publisher_metadata")
    };

    private readonly AgentRunner _runner;

    public CopilotAgent(AgentRunner runner) => _runner = runner;

    public static bool Supports(string task) => Prompts.ContainsKey(task);

    public async Task<CopilotResponse> RunAsync(CopilotRequest req, CancellationToken ct)
    {
        var prompt = Prompts[req.Task];
        if (req.Task == CopilotTasks.PublisherMetadata) return await RunMetadataAsync(prompt, req, ct);
        var line = req.Line!;
        var speaker = req.Bible.FirstOrDefault(b => b.EntryId == line.SpeakerId);

        var sb = new StringBuilder();
        sb.AppendLine($"[[agent:copilot]] [[task:{req.Task}]]");
        sb.AppendLine("## Bible liên quan (dữ liệu)");
        foreach (var b in req.Bible)
            sb.AppendLine($"- code={b.Code} | category={b.Category} | {Flat(b.EvidenceText)}");
        if (req.Bible.Count == 0) sb.AppendLine("(không có)");
        sb.AppendLine().AppendLine("## Dòng xung quanh (dữ liệu, không sửa)");
        foreach (var n in line.Neighbors) sb.AppendLine($"- \"{Flat(n)}\"");
        sb.AppendLine().AppendLine("## Dòng cần xử lý (dữ liệu)");
        sb.AppendLine($"speaker={speaker?.Code ?? "-"} | stage=\"{Flat(line.StageDirection)}\"");
        if (req.MaxChars is { } max) sb.AppendLine($"maxChars={max} | length={line.Dialogue.Trim().Length}");
        sb.AppendLine($"dialogue=\"{Flat(line.Dialogue)}\"");
        if (!string.IsNullOrWhiteSpace(req.Instruction))
            sb.AppendLine().AppendLine("## Chỉ dẫn của người dùng (dữ liệu)").AppendLine(Flat(req.Instruction));
        sb.AppendLine().AppendLine("Hãy trả về JSON theo đúng định dạng đã nêu.");

        var call = await _runner.RunAsync<CopilotOutput>(prompt, sb.ToString(), text =>
        {
            var output = OutputParser.Parse<CopilotOutput>(text, out var error);
            if (output is null) return (null, [error!]);
            var errors = CopilotRules.Validate(req.Task, line.Dialogue, output.Text, req.MaxChars);
            return errors.Count == 0 ? (output with { Text = output.Text!.Trim(), Reason = output.Reason?.Trim() }, []) : (null, errors);
        }, ct);

        return new CopilotResponse(call.Succeeded, call.Payload?.Text, call.Payload?.Reason, prompt.Version, prompt.Ref,
            call.PromptTokens, call.CompletionTokens, call.RepairAttempts, call.StartedAtUtc, call.CompletedAtUtc,
            call.ErrorCode, call.ErrorMessage);
    }

    private async Task<CopilotResponse> RunMetadataAsync(PromptInfo prompt, CopilotRequest req, CancellationToken ct)
    {
        var series = req.Series!;
        var sb = new StringBuilder();
        sb.AppendLine($"[[agent:copilot]] [[task:{req.Task}]]");
        sb.AppendLine("## Series hiện tại (dữ liệu)");
        sb.AppendLine($"title=\"{Flat(series.Title)}\" | format={series.Format} | genre=\"{Flat(series.Genre)}\"");
        sb.AppendLine($"synopsis=\"{Flat(series.Synopsis)}\"");
        sb.AppendLine($"storyArc=\"{Flat(series.StoryArc)}\"");
        sb.AppendLine().AppendLine("## Dàn ý các chương (dữ liệu)");
        foreach (var o in series.ChapterOutlines) sb.AppendLine($"- {Flat(o)}");
        if (series.ChapterOutlines.Count == 0) sb.AppendLine("(chưa có)");
        sb.AppendLine().AppendLine("## Nhân vật chính trong Bible (dữ liệu)");
        foreach (var b in req.Bible) sb.AppendLine($"- {b.Name}: {Flat(b.Description)}");
        if (!string.IsNullOrWhiteSpace(req.Instruction))
            sb.AppendLine().AppendLine("## Chỉ dẫn của người dùng (dữ liệu)").AppendLine(Flat(req.Instruction));
        sb.AppendLine().AppendLine("Hãy trả về JSON theo đúng định dạng đã nêu.");

        var call = await _runner.RunAsync<(ReleaseMetadata Meta, string? Reason)?>(prompt, sb.ToString(), text =>
        {
            var o = OutputParser.Parse<MetadataOutput>(text, out var error);
            if (o is null) return (null, [error!]);
            var meta = new ReleaseMetadata(o.Logline?.Trim() ?? "", o.Synopsis?.Trim() ?? "", o.Genre?.Trim() ?? "",
                o.Teaser?.Trim() ?? "", o.AgeRating?.Trim() ?? "");
            var errors = CopilotRules.ValidateMetadata(meta);
            return errors.Count == 0 ? ((meta, o.Reason?.Trim()), []) : (null, errors);
        }, ct);

        return new CopilotResponse(call.Succeeded, null, call.Payload?.Reason, prompt.Version, prompt.Ref,
            call.PromptTokens, call.CompletionTokens, call.RepairAttempts, call.StartedAtUtc, call.CompletedAtUtc,
            call.ErrorCode, call.ErrorMessage, call.Payload?.Meta);
    }

    private static string Flat(string? s) => (s ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
}
