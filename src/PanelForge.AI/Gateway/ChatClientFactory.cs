using System.ClientModel;
using System.Net;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using PanelForge.AI.Contracts;
using PanelForge.AI.Demo;

namespace PanelForge.AI.Gateway;

/// <summary>Tạo IChatClient cho một run. Key Gemini chỉ đi từ job vào client trong bộ nhớ; không lưu, không log.</summary>
public sealed class ChatClientFactory
{
    private readonly ProviderOptions _options;
    private readonly LlmResilience _resilience;
    private readonly IHttpClientFactory _httpFactory;

    public ChatClientFactory(IOptions<ProviderOptions> options, LlmResilience resilience, IHttpClientFactory httpFactory)
    {
        _options = options.Value;
        _resilience = resilience;
        _httpFactory = httpFactory;
    }

    public IChatClient Create(AiRunJob job)
    {
        if (job.Provider == AiProtocol.ProviderMock) return new MockChatClient();

        if (job.Provider != AiProtocol.ProviderGemini || string.IsNullOrWhiteSpace(job.ApiKey))
            throw new InvalidOperationException("Provider không hỗ trợ hoặc thiếu API key.");

        var client = new OpenAIClient(
            new ApiKeyCredential(job.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(_options.GeminiOpenAiEndpoint) });
        return new LlmGateway(client.GetChatClient(job.Model).AsIChatClient(), _resilience);
    }

    /// <summary>
    /// Kiểm tra model Gemini còn khả dụng (models.get) TRƯỚC khi chạy workflow.
    /// Trả null nếu khả dụng, ngược lại (mã lỗi, thông điệp) — không bao giờ tự chuyển sang Mock.
    /// </summary>
    public async Task<(string Code, string Message)?> ProbeGeminiModelAsync(AiRunJob job, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient("gemini-probe");
        using var request = new HttpRequestMessage(HttpMethod.Get, _options.GeminiModelsUrl.TrimEnd('/') + "/" + Uri.EscapeDataString(job.Model));
        request.Headers.Add("x-goog-api-key", job.ApiKey);
        try
        {
            using var response = await http.SendAsync(request, ct);
            return response.StatusCode switch
            {
                HttpStatusCode.OK => null,
                HttpStatusCode.NotFound => ("MODEL_UNAVAILABLE", $"Model '{job.Model}' không tồn tại/không còn khả dụng (HTTP 404). Đổi AiService:GeminiModel."),
                HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    ("PROVIDER_AUTH_FAILED", $"Gemini từ chối API key (HTTP {(int)response.StatusCode})."),
                _ => ("PROVIDER_UNAVAILABLE", $"Gemini trả HTTP {(int)response.StatusCode} khi kiểm tra model.")
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ("PROVIDER_UNAVAILABLE", "Không kết nối được Gemini để kiểm tra model.");
        }
    }
}