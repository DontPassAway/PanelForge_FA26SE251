using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using PanelForge.AI.Contracts;
using PanelForge.AI.Gateway;

namespace PanelForge.AI.Agents;

public sealed record PromptInfo(string Name, string Text, string Version, string Ref);

/// <summary>Prompt nằm trong file .md nhúng vào assembly; PromptRef = tên + version + hash nội dung để truy vết đúng bản đã dùng.</summary>
public static class PromptLibrary
{
    public static PromptInfo Load(string name)
    {
        var asm = typeof(PromptLibrary).Assembly;
        var resource = asm.GetManifestResourceNames().Single(n => n.EndsWith($".{name}.md", StringComparison.OrdinalIgnoreCase));
        using var reader = new StreamReader(asm.GetManifestResourceStream(resource)!, Encoding.UTF8);
        var text = reader.ReadToEnd();

        var m = System.Text.RegularExpressions.Regex.Match(text, @"<!--\s*version:\s*(\d+)\s*-->");
        var version = m.Success ? m.Groups[1].Value : "0";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12].ToLowerInvariant();
        return new PromptInfo(name, text, $"{name}@{version}", $"Prompts/{name}.md@v{version}#sha256:{hash}");
    }
}

public sealed record AgentCall<T>(
    bool Succeeded, T? Payload, string? OutputJson, string? ErrorCode, string? ErrorMessage,
    long? PromptTokens, long? CompletionTokens, int RepairAttempts, DateTime StartedAtUtc, DateTime CompletedAtUtc,
    string ParametersJson);

/// <summary>
/// Chạy một agent: ChatClientAgent (Microsoft Agent Framework) trên IChatClient → parse JSON → validate bằng code →
/// nếu sai cho phép sửa output (mặc định MỘT lần); sau đó ghi lỗi rõ ràng. Không có tool-call tự do: dữ liệu vào là context đã chụp.
/// </summary>
public sealed class AgentRunner
{
    public const int MaxRepairAttempts = 1;

    /// <summary>Chỉ đạo của Supervisor cho lần chạy agent kế tiếp (workflow gán trước mỗi bước, tuần tự nên không đua).</summary>
    public string? Instruction { get; set; }

    /// <summary>Trần token đầu ra cho bước nhẹ (Supervisor, Copilot): output là một JSON nhỏ.</summary>
    public const int LightMaxOutputTokens = 2048;

    private readonly IChatClient _chat;
    private readonly ProviderOptions _options;
    private readonly string? _model;

    public AgentRunner(IChatClient chat, IOptions<ProviderOptions> options, string? model = null)
    {
        _chat = chat;
        _options = options.Value;
        _model = model;
    }

    /// <summary>
    /// Mức "thinking" cho bước nhẹ. Token thinking của Gemini 2.5 bị tính phí và trừ vào MaxOutputTokens;
    /// bước chỉ chọn hành động/viết lại một dòng không cần. 2.5 Pro không tắt hẳn được → "low".
    /// </summary>
    internal static string LightReasoningEffort(string? model) =>
        model?.Contains("pro", StringComparison.OrdinalIgnoreCase) == true ? "low" : "none";

    /// <param name="light">Bước nhẹ: tắt/giảm thinking và hạ trần token đầu ra.</param>
    /// <param name="lowReasoning">
    /// Bước nặng có output dài (Writer): thinking "low" để token thinking không ăn hết MaxOutputTokens và cắt cụt JSON.
    /// </param>
    public async Task<AgentCall<T>> RunAsync<T>(
        PromptInfo prompt, string userMessage,
        Func<string, (T? Payload, IReadOnlyList<string> Errors)> parse, CancellationToken ct,
        int maxRepairAttempts = MaxRepairAttempts, bool light = false, bool lowReasoning = false)
    {
        var started = DateTime.UtcNow;
        long? inTokens = null, outTokens = null;
        var maxOutputTokens = light ? Math.Min(LightMaxOutputTokens, _options.MaxOutputTokens) : _options.MaxOutputTokens;
        var effort = light ? LightReasoningEffort(_model) : lowReasoning ? "low" : null;
        var parameters = AiJson.Serialize(new
        {
            temperature = _options.Temperature, maxOutputTokens,
            responseFormat = "json", maxRepairAttempts, reasoningEffort = effort
        });
        // Chỉ đạo của Supervisor cho agent này (nếu có): đưa vào cuối tin nhắn, đánh dấu rõ là dữ liệu điều phối
        if (!string.IsNullOrWhiteSpace(Instruction))
            userMessage += $"\n\n## Chỉ đạo của Supervisor cho bước này (dữ liệu điều phối, không thay đổi định dạng đầu ra)\n{Instruction}";
        var message = userMessage;
        IReadOnlyList<string> errors = [];

        for (var attempt = 0; attempt <= maxRepairAttempts; attempt++)
        {
            try
            {
                var agent = new ChatClientAgent(_chat, instructions: prompt.Text, name: prompt.Name);
                var response = await agent.RunAsync(message, session: null,
                    options: new ChatClientAgentRunOptions(new ChatOptions
                    {
                        Temperature = _options.Temperature,
                        MaxOutputTokens = maxOutputTokens,
                        ResponseFormat = ChatResponseFormat.Json,
                        // Gemini OpenAI-compat nhận reasoning_effort "none"/"low"
                        Reasoning = effort is null ? null : new ReasoningOptions
                        {
                            Effort = effort == "none" ? ReasoningEffort.None : ReasoningEffort.Low
                        }
                    }), cancellationToken: ct);

                if (response.Usage is { } usage)
                {
                    inTokens = (inTokens ?? 0) + (usage.InputTokenCount ?? 0);
                    outTokens = (outTokens ?? 0) + (usage.OutputTokenCount ?? 0);
                }

                string? json;
                T? payload;
                (payload, errors) = parse(response.Text);
                if (errors.Count == 0 && payload is not null)
                {
                    json = AiJson.Serialize(payload);
                    return new AgentCall<T>(true, payload, json, null, null, inTokens, outTokens, attempt,
                        started, DateTime.UtcNow, parameters);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                var (code, msg) = LlmGateway.Classify(ex);
                return Failure<T>(code, msg, inTokens, outTokens, attempt, started, parameters);
            }
            catch (OperationCanceledException)
            {
                return Failure<T>("RUN_TIMEOUT", "Run bị huỷ hoặc quá thời gian.", inTokens, outTokens, attempt, started, parameters);
            }

            message = userMessage + "\n\n[[repair]] Output trước KHÔNG hợp lệ: "
                      + string.Join(" | ", errors.Take(8))
                      + "\nTrả lại MỘT đối tượng JSON đúng định dạng đã nêu, sửa các lỗi trên.";
        }

        return Failure<T>("INVALID_OUTPUT",
            "AI trả kết quả không hợp lệ (đã thử sửa " + maxRepairAttempts + " lần):\n• " + string.Join("\n• ", errors.Take(6)),
            inTokens, outTokens, maxRepairAttempts, started, parameters);
    }

    private static AgentCall<T> Failure<T>(
        string code, string message, long? inTokens, long? outTokens, int repairs, DateTime started, string parameters) =>
        new(false, default, null, code, message, inTokens, outTokens, repairs, started, DateTime.UtcNow, parameters);
}