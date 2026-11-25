using Microsoft.Extensions.AI;
using PanelForge.AI.Contracts;
using PanelForge.AI.Gateway;
using PanelForge.AI.Validators;

namespace PanelForge.AI.Agents;

public sealed record VisionOutput(decimal? Score, List<VisionDeviation>? Deviations, string? SummaryReason);

/// <summary>
/// So artwork panel với character sheet (một lần gọi model vision qua cùng LlmGateway: timeout/retry/circuit breaker/giãn cách).
/// Không đọc được JSON hoặc thiếu score → Succeeded = false, không đoán điểm.
/// </summary>
public sealed class VisionAgent(IChatClient chat)
{
    public static readonly PromptInfo Prompt = PromptLibrary.Load("vision_compare");
    private static readonly string[] Aspects = ["hair", "face", "outfit", "accessory"];

    public async Task<VisionCompareResponse> RunAsync(VisionCompareRequest req, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        VisionCompareResponse Fail(string code, string message, long? inTok = null, long? outTok = null) =>
            new(false, null, [], null, req.Model, inTok, outTok, started, DateTime.UtcNow, code, message);

        var user = new ChatMessage(ChatRole.User,
        [
            new TextContent($"[[agent:vision]] Nhân vật (dữ liệu): \"{req.CharacterName.Replace('\n', ' ').Trim()}\". Ảnh 1 = character sheet, Ảnh 2 = artwork panel."),
            new TextContent("Ảnh 1:"),
            new DataContent(Convert.FromBase64String(req.CharacterSheet.Base64), req.CharacterSheet.MimeType),
            new TextContent("Ảnh 2:"),
            new DataContent(Convert.FromBase64String(req.Artwork.Base64), req.Artwork.MimeType),
            new TextContent("Hãy trả về JSON theo đúng định dạng đã nêu.")
        ]);

        ChatResponse response;
        try
        {
            response = await chat.GetResponseAsync([new ChatMessage(ChatRole.System, Prompt.Text), user],
                new ChatOptions { Temperature = 0.1f, ResponseFormat = ChatResponseFormat.Json }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var (code, message) = LlmGateway.Classify(ex);
            return Fail(code, message);
        }

        var inTok = response.Usage?.InputTokenCount;
        var outTok = response.Usage?.OutputTokenCount;
        var output = OutputParser.Parse<VisionOutput>(response.Text, out var error);
        if (output?.Score is not { } score || score is < 0 or > 1)
            return Fail("INVALID_OUTPUT", output is null ? $"Kết quả vision không đọc được: {error}" : "Kết quả vision thiếu score 0..1.", inTok, outTok);

        var deviations = (output.Deviations ?? [])
            .Where(d => !string.IsNullOrWhiteSpace(d.Description))
            .Select(d => d with { Aspect = Aspects.Contains(d.Aspect?.ToLowerInvariant()) ? d.Aspect!.ToLowerInvariant() : "general" })
            .Take(10).ToList();
        return new VisionCompareResponse(true, Math.Round(score, 2), deviations, output.SummaryReason?.Trim(), req.Model,
            inTok, outTok, started, DateTime.UtcNow, null, null);
    }
}
