using System.ClientModel;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Net;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using PanelForge.AI.Contracts;

namespace PanelForge.AI.Gateway;

/// <summary>Tạo IChatClient cho một run. Key Gemini chỉ đi từ job vào client trong bộ nhớ; không lưu, không log.</summary>
public sealed class ChatClientFactory
{
    private readonly ProviderOptions _options;
    private readonly IOptions<ProviderOptions> _optionsAccessor;
    // ponytail: giữ theo vòng đời process, số key thực tế nhỏ nên không cần dọn.
    private readonly ConcurrentDictionary<string, LlmResilience> _resilienceByKey = new();
    private readonly IHttpClientFactory _httpFactory;

    private readonly ILogger<LlmResilience>? _resilienceLogger;

    public ChatClientFactory(IOptions<ProviderOptions> options, IHttpClientFactory httpFactory,
        ILogger<LlmResilience>? resilienceLogger = null)
    {
        _resilienceLogger = resilienceLogger;
        _options = options.Value;
        _optionsAccessor = options;
        _httpFactory = httpFactory;
    }

    public IChatClient Create(AiRunJob job)
    {
        if (!AiProtocol.CredentialsOk(job.Provider, job.ApiKey, job.AccountId))
            throw new InvalidOperationException("Provider không hỗ trợ hoặc thiếu API key/AccountId.");

        var client = new OpenAIClient(
            new ApiKeyCredential(job.ApiKey!),
            new OpenAIClientOptions { Endpoint = EndpointFor(job) });
        return new LlmGateway(client.GetChatClient(job.Model).AsIChatClient(), ResilienceFor(job.ApiKey!));
    }

    /// <summary>Cả Gemini và Cloudflare Workers AI đều có endpoint OpenAI-compatible: chỉ khác địa chỉ gốc.</summary>
    public Uri EndpointFor(AiRunJob job) => job.Provider == AiProtocol.ProviderCloudflare
        ? new Uri($"{_options.CloudflareBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(job.AccountId!.Trim())}/ai/v1/")
        : new Uri(_options.GeminiOpenAiEndpoint);

    /// <summary>Giới hạn tần suất và circuit breaker tính theo từng key: đổi key mới không bị key cũ chặn.</summary>
    public LlmResilience ResilienceFor(string apiKey) =>
        _resilienceByKey.GetOrAdd(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey.Trim()))),
            _ => new LlmResilience(_optionsAccessor, _resilienceLogger));

    /// <summary>
    /// Kiểm tra model Gemini còn khả dụng (models.get) TRƯỚC khi chạy workflow.
    /// Trả null nếu khả dụng, ngược lại (mã lỗi, thông điệp).
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