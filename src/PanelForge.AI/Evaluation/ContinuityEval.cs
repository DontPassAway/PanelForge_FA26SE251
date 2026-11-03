using System.ClientModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using PanelForge.AI.Agents;
using PanelForge.AI.Contracts;
using PanelForge.AI.Demo;
using PanelForge.AI.Gateway;

namespace PanelForge.AI.Evaluation;

public sealed record EvalSample(
    [property: JsonPropertyName("ID")] string Id,
    [property: JsonPropertyName("Chapter")] string Chapter,
    [property: JsonPropertyName("Category")] string Category,
    [property: JsonPropertyName("Modified Evaluation Sample")] string Sample,
    [property: JsonPropertyName("Seeded Error Type")] string SeededErrorType,
    [property: JsonPropertyName("Verification Status")] string? VerificationStatus);

public sealed record EvalResult(
    string Id, string Category, string SeededErrorType, bool Expected, bool? Predicted,
    string? ErrorCode, IReadOnlyList<string> Findings, long? Tokens, double Seconds);

public sealed record EvalMetrics(int Tp, int Fp, int Tn, int Fn, int Failed)
{
    public int Scored => Tp + Fp + Tn + Fn;
    public double Accuracy => Scored == 0 ? 0 : (double)(Tp + Tn) / Scored;
    public double Precision => Tp + Fp == 0 ? 0 : (double)Tp / (Tp + Fp);
    public double Recall => Tp + Fn == 0 ? 0 : (double)Tp / (Tp + Fn);
    public double F1 => Precision + Recall == 0 ? 0 : 2 * Precision * Recall / (Precision + Recall);
    public double FalsePositiveRate => Fp + Tn == 0 ? 0 : (double)Fp / (Fp + Tn);
}

/// <summary>
/// Chấm Continuity Agent trên bộ 80 mẫu có lỗi gieo sẵn (docs/eval). Mỗi mẫu = một ScriptLine, đi qua ĐÚNG đường
/// production (prompt continuity.md, AgentRunner, ContentRules, LlmGateway) — không qua DB/Backend.
/// Chạy: dotnet run --project src/PanelForge.AI -- eval [--dataset f] [--bible f] [--out f] [--limit n] [--only SMP-001,..] [--model m] [--mock]
/// Key Gemini lấy từ biến môi trường GEMINI_API_KEY.
/// </summary>
public static class ContinuityEval
{
    // Code Bible: CHR-LAM, PRP-SO-TRO, FACT-01, RULE-HOA-01, STY-VISUAL...
    private static readonly Regex RowRx = new(@"^\|\s*([A-Z]{2,5}-[A-Z0-9-]+)\s*\|(.+)\|\s*$", RegexOptions.Compiled);
    private static readonly Regex SpeakerRx = new(@"Speaker:\s*(CHR-[A-Z-]+)", RegexOptions.Compiled);
    private static readonly Regex DialogueRx = new("Thoại:\\s*\"(.*)\"\\s*$", RegexOptions.Compiled);
    private static readonly Regex ChapterRx = new(@"(\d+)", RegexOptions.Compiled);

    /// <summary>Dòng bảng có Code → một mục Bible; ghi chú dưới bảng nhân vật, mục 2 (Mục lục sản xuất), mục 7 (Checklist) → mục tự do.</summary>
    public static List<BibleEntryContext> ParseBible(string markdown)
    {
        var entries = new List<BibleEntryContext>();
        void Add(string code, string category, string name, string desc) =>
            entries.Add(new BibleEntryContext(Guid.NewGuid(), code, category, name, desc.Trim(), Guid.NewGuid(), 1, 1));

        var lines = markdown.Replace("\r", "").Split('\n');
        string? section = null, sub = null;
        var notes = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.StartsWith("## ")) section = line;
            if (line.StartsWith("### ")) sub = line;
            if (section?.StartsWith("## 1.") != true) continue;

            var m = RowRx.Match(line);
            if (m.Success)
            {
                var code = m.Groups[1].Value;
                var cells = m.Groups[2].Value.Split('|').Select(c => c.Trim()).ToArray();
                var category = code.Split('-')[0] switch
                {
                    "CHR" => "Character", "LOC" => "Location", "PRP" => "Item", "TRM" => "Term", "FACT" => "PlotFact", _ => "WorldRule"
                };
                // Bảng CHR/LOC/PRP/TRM có cột tên; RULE/STY/FACT không có
                var hasName = category is "Character" or "Location" or "Item" or "Term";
                Add(code, category, hasName ? cells[0] : code, string.Join(" | ", hasName ? cells.Skip(1) : cells));
            }
            else if (sub?.StartsWith("### 1.1") == true && line.Trim().Length > 0 && !line.StartsWith('|') && !line.StartsWith('#'))
                notes.AppendLine(line);
        }

        if (notes.Length > 0) Add("NOTE-CHARACTERS", "WorldRule", "Ghi chú nhân vật", notes.ToString());
        Add("PROD-INDEX", "WorldRule", "Mục lục sản xuất (cảnh, ngày, nhân vật có mặt)", SectionText(lines, "## 2."));
        Add("CHECKLIST", "WorldRule", "Checklist liên tục khi vẽ và duyệt", SectionText(lines, "## 7."));
        return entries;
    }

    private static string SectionText(string[] lines, string heading) =>
        string.Join("\n", lines.SkipWhile(l => !l.StartsWith(heading)).Skip(1).TakeWhile(l => !l.StartsWith("## "))
            .Where(l => l.Trim().Length > 0 && !l.StartsWith("|---")));

    /// <summary>Một mẫu → context + bản nháp một dòng (speaker, thoại, phần còn lại là chỉ dẫn cảnh).</summary>
    public static (AgentContext Ctx, ScriptDraft Draft) BuildCase(EvalSample s, IReadOnlyList<BibleEntryContext> bible, string model)
    {
        var chapter = ChapterRx.Match(s.Chapter) is { Success: true } cm ? int.Parse(cm.Value) : 1;
        var speakerCode = SpeakerRx.Match(s.Sample) is { Success: true } sm ? sm.Groups[1].Value : null;
        var speakerId = bible.FirstOrDefault(b => b.Category == "Character" && b.Code == speakerCode)?.EntryId;
        var dm = DialogueRx.Match(s.Sample);
        var dialogue = dm.Success ? dm.Groups[1].Value : null;
        var stage = (dm.Success ? s.Sample[..dm.Index] : s.Sample).Trim();

        var line = new BaselineScriptLine(Guid.NewGuid(), 1, speakerId, dialogue, stage);
        var ctx = new AgentContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), chapter, Guid.NewGuid(), s.Id, null,
            "Kiểm tra liên tục một dòng script/visual prompt", "LTR", 1, AiProtocol.ProviderGemini, model, 0,
            bible, bible.Select(b => b.RevisionId).ToList(), [line]);
        return (ctx, PromptText.BaselineDraft(ctx));
    }

    /// <summary>Có lỗi = ít nhất một finding Blocking/Warning (Info không tính). Mẫu agent lỗi không vào ma trận.</summary>
    public static EvalMetrics Score(IEnumerable<EvalResult> results)
    {
        int tp = 0, fp = 0, tn = 0, fn = 0, failed = 0;
        foreach (var r in results)
        {
            if (r.Predicted is not { } p) { failed++; continue; }
            if (r.Expected && p) tp++; else if (!r.Expected && p) fp++; else if (!r.Expected) tn++; else fn++;
        }
        return new EvalMetrics(tp, fp, tn, fn, failed);
    }

    public static async Task<int> RunAsync(string[] args)
    {
        string Arg(string name, string fallback)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }
        var root = FindRepoRoot();
        var datasetPath = Arg("--dataset", Path.Combine(root, "docs", "eval", "evaluation_dataset_80_patched.json"));
        var biblePath = Arg("--bible", Path.Combine(root, "docs", "eval", "dom-lua-truong-sinh.md"));
        // dotnet run đặt cwd = thư mục project → đường dẫn tương đối tính theo gốc Backend
        var outPath = Path.GetFullPath(Arg("--out", Path.Combine("docs", "eval", $"result-{DateTime.Now:yyyyMMdd-HHmmss}.json")), root);
        var resumePath = Arg("--resume", "");
        var model = Arg("--model", "gemini-2.5-flash");
        var limit = int.Parse(Arg("--limit", "0"));
        var only = Arg("--only", "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
        var mock = args.Contains("--mock");

        var samples = JsonSerializer.Deserialize<List<EvalSample>>(await File.ReadAllTextAsync(datasetPath))!
            .Where(s => only.Count == 0 || only.Contains(s.Id)).ToList();
        if (limit > 0) samples = samples.Take(limit).ToList();
        var bible = ParseBible(await File.ReadAllTextAsync(biblePath));

        var options = Options.Create(new ProviderOptions { MinCallIntervalMs = mock ? 0 : 6500, Temperature = 0f });
        IChatClient chat;
        if (mock) chat = new MockChatClient();
        else
        {
            // --endpoint: provider OpenAI-compatible khác (Groq, OpenRouter...) chỉ để so sánh; key từ AI_API_KEY
            var endpoint = Arg("--endpoint", options.Value.GeminiOpenAiEndpoint);
            var key = Environment.GetEnvironmentVariable("AI_API_KEY") ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            if (string.IsNullOrWhiteSpace(key)) { Console.Error.WriteLine("Thiếu AI_API_KEY/GEMINI_API_KEY (hoặc dùng --mock)."); return 2; }
            var client = new OpenAIClient(new ApiKeyCredential(key), new OpenAIClientOptions { Endpoint = new Uri(endpoint) });
            chat = new LlmGateway(client.GetChatClient(model).AsIChatClient(), new LlmResilience(options));
        }
        var agent = new ContinuityAgent(new AgentRunner(chat, options));

        Console.WriteLine($"Bible: {bible.Count} mục | Mẫu: {samples.Count} | Model: {(mock ? "mock" : model)} | Prompt: {ContinuityAgent.Prompt.Ref}");
        // --resume: giữ kết quả đã chấm (Predicted != null) của lần trước, chỉ chạy mẫu còn thiếu/lỗi
        var results = new List<EvalResult>();
        if (resumePath.Length > 0)
        {
            using var prev = JsonDocument.Parse(await File.ReadAllTextAsync(Path.GetFullPath(resumePath, root)));
            results.AddRange(prev.RootElement.GetProperty("results").Deserialize<List<EvalResult>>(JsonWeb)!
                .Where(r => r.Predicted is not null && samples.Any(s => s.Id == r.Id)));
            Console.WriteLine($"Resume: giữ {results.Count} mẫu đã chấm.");
        }
        var consecutiveProviderFailures = 0;
        foreach (var s in samples.Where(s => results.All(r => r.Id != s.Id)))
        {
            // Hết quota ngày (429 kéo dài) → dừng sớm, vẫn ghi phần đã chạy để --resume hôm sau
            if (consecutiveProviderFailures >= 3) { Console.WriteLine("Provider lỗi liên tiếp (thường là hết quota) → dừng, chạy lại với --resume."); break; }
            var (ctx, draft) = BuildCase(s, bible, model);
            var call = (await agent.RunAsync(ctx, new KeyMaps(ctx), draft, CancellationToken.None)).Call;
            var findings = call.Payload?.Findings ?? [];
            bool? predicted = call.Succeeded ? findings.Any(f => f.Severity is "Blocking" or "Warning") : null;
            var r = new EvalResult(s.Id, s.Category, s.SeededErrorType, s.SeededErrorType != "None", predicted,
                call.ErrorCode, findings.Select(f => $"[{f.Severity}/{f.Category}] {f.Description}").ToList(),
                (call.PromptTokens ?? 0) + (call.CompletionTokens ?? 0),
                (call.CompletedAtUtc - call.StartedAtUtc).TotalSeconds);
            consecutiveProviderFailures = call.ErrorCode is "PROVIDER_UNAVAILABLE" or "QUOTA_EXCEEDED" ? consecutiveProviderFailures + 1 : 0;
            if (consecutiveProviderFailures == 0) results.Add(r);
            var mark = predicted is null ? $"FAIL {call.ErrorCode}" : predicted == r.Expected ? "ok" : "MISS";
            Console.WriteLine($"{s.Id} {s.Category,-17} expected={(r.Expected ? "err" : "clean"),-5} -> {mark}");
        }

        results = results.OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
        var missing = samples.Count - results.Count;
        if (missing > 0) Console.WriteLine($"CHƯA ĐỦ: còn {missing} mẫu chưa chạy (provider lỗi) — số liệu dưới đây chỉ trên {results.Count} mẫu.");
        var overall = Score(results);
        var byCategory = results.GroupBy(r => r.Category).ToDictionary(g => g.Key, g => Score(g));
        var byErrorType = results.GroupBy(r => r.SeededErrorType).ToDictionary(g => g.Key, g => Score(g));
        Console.WriteLine();
        Console.WriteLine($"TỔNG: acc={overall.Accuracy:P1} precision={overall.Precision:P1} recall={overall.Recall:P1} F1={overall.F1:P1} FPR={overall.FalsePositiveRate:P1} lỗi agent={overall.Failed}");
        foreach (var (cat, m) in byCategory)
            Console.WriteLine($"  {cat,-17} acc={m.Accuracy:P1} recall={m.Recall:P1} FPR={m.FalsePositiveRate:P1} (TP {m.Tp} FP {m.Fp} TN {m.Tn} FN {m.Fn} fail {m.Failed})");

        var report = new
        {
            ranAt = DateTime.UtcNow, model = mock ? "mock" : model, promptRef = ContinuityAgent.Prompt.Ref,
            dataset = Path.GetFileName(datasetPath), bibleEntries = bible.Count, samples = samples.Count, notRun = missing,
            rule = "predicted error = >=1 finding Blocking/Warning",
            overall = Metrics(overall), byCategory = byCategory.ToDictionary(kv => kv.Key, kv => Metrics(kv.Value)),
            byErrorType = byErrorType.ToDictionary(kv => kv.Key, kv => Metrics(kv.Value)),
            totalTokens = results.Sum(r => r.Tokens ?? 0), results
        };
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        await File.WriteAllTextAsync(outPath, JsonSerializer.Serialize(report, JsonWeb));
        Console.WriteLine($"Đã ghi {outPath}");
        return 0;
    }

    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static object Metrics(EvalMetrics m) => new
    {
        m.Tp, m.Fp, m.Tn, m.Fn, m.Failed,
        accuracy = Math.Round(m.Accuracy, 4), precision = Math.Round(m.Precision, 4), recall = Math.Round(m.Recall, 4),
        f1 = Math.Round(m.F1, 4), falsePositiveRate = Math.Round(m.FalsePositiveRate, 4)
    };

    private static string FindRepoRoot()
    {
        for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "docs", "eval"))) return d.FullName;
        return Directory.GetCurrentDirectory();
    }
}
