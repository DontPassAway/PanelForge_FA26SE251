using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Models;

namespace PanelForge.Infrastructure.ExternalServices;

/// <summary>
/// Client mỏng gọi PanelForge.AI (HTTP + timeout + retry + circuit breaker cấu hình ở DependencyInjection).
/// AI tắt/chậm/mạch mở → trả Accepted = false + AI_UNAVAILABLE, không ném exception: chức năng Backend khác không bị ảnh hưởng.
/// Tuyệt đối không log AiRunJob (chứa API key).
/// </summary>
public sealed class AiClient : IAiClient
{
    public const string CopilotHttpClient = "ai-copilot";
    public const string ImageHttpClient = "ai-image";

    private readonly HttpClient _http;
    private readonly IHttpClientFactory _httpFactory;
    private readonly AiAgentSettings _settings;
    private readonly ILogger<AiClient> _logger;

    public AiClient(HttpClient http, IHttpClientFactory httpFactory, AiAgentSettings settings, ILogger<AiClient> logger)
    {
        _http = http;
        _httpFactory = httpFactory;
        _settings = settings;
        _logger = logger;
    }

    public async Task<AiRunAck> StartRunAsync(AiRunJob job, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ServiceKey))
            return new AiRunAck(false, ResultErrorCodes.AiUnavailable, "Chưa cấu hình AiService:ServiceKey (user-secrets).");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "runs") { Content = JsonContent.Create(job, options: AiJson.Options) };
            request.Headers.Add(AiProtocol.ServiceKeyHeader, _settings.ServiceKey);

            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var ack = await response.Content.ReadFromJsonAsync<AiRunAck>(AiJson.Options, cancellationToken);
                return ack ?? new AiRunAck(false, ResultErrorCodes.AiUnavailable, "AI service trả phản hồi rỗng.");
            }

            _logger.LogWarning("AI service từ chối job {RunId}: HTTP {Status}", job.RunId, (int)response.StatusCode);
            return new AiRunAck(false, ResultErrorCodes.AiUnavailable,
                response.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                    ? "Hàng đợi của AI service đang đầy, thử lại sau."
                    : $"AI service từ chối job (HTTP {(int)response.StatusCode}).");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException
                                       or Polly.CircuitBreaker.BrokenCircuitException or Polly.Timeout.TimeoutRejectedException
                                       or System.Text.Json.JsonException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            _logger.LogWarning("AI service không khả dụng ({Type}) khi giao run {RunId}", ex.GetType().Name, job.RunId);
            return new AiRunAck(false, ResultErrorCodes.AiUnavailable, "AI service đang tắt hoặc không phản hồi.");
        }
    }

    public async Task<CopilotResponse> RunCopilotAsync(CopilotRequest request, CancellationToken cancellationToken = default)
    {
        var started = DateTime.UtcNow;
        CopilotResponse Unavailable(string message) =>
            new(false, null, null, "", "", null, null, 0, started, DateTime.UtcNow, ResultErrorCodes.AiUnavailable, message);

        if (string.IsNullOrWhiteSpace(_settings.ServiceKey)) return Unavailable("Chưa cấu hình AiService:ServiceKey (user-secrets).");
        try
        {
            var http = _httpFactory.CreateClient(CopilotHttpClient);
            using var message = new HttpRequestMessage(HttpMethod.Post, "copilot/" + Uri.EscapeDataString(request.Task))
            {
                Content = JsonContent.Create(request, options: AiJson.Options)
            };
            message.Headers.Add(AiProtocol.ServiceKeyHeader, _settings.ServiceKey);
            using var response = await http.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AI service từ chối Copilot {Task}: HTTP {Status}", request.Task, (int)response.StatusCode);
                return Unavailable($"AI service từ chối yêu cầu Copilot (HTTP {(int)response.StatusCode}).");
            }
            return await response.Content.ReadFromJsonAsync<CopilotResponse>(AiJson.Options, cancellationToken)
                   ?? Unavailable("AI service trả phản hồi rỗng.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException or System.Text.Json.JsonException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            _logger.LogWarning("Copilot {Task} lỗi/timeout ({Type})", request.Task, ex.GetType().Name);
            return Unavailable(ex is TaskCanceledException or TimeoutException
                ? "AI không phản hồi trong thời gian cho phép, thử lại sau."
                : "AI service đang tắt hoặc không phản hồi.");
        }
    }

    public async Task<ImageGenResponse> GenerateImageAsync(ImageGenRequest request, CancellationToken cancellationToken = default)
    {
        var started = DateTime.UtcNow;
        ImageGenResponse Unavailable(string message) =>
            new(false, null, null, false, null, null, started, DateTime.UtcNow, ResultErrorCodes.AiUnavailable, message);

        if (string.IsNullOrWhiteSpace(_settings.ServiceKey)) return Unavailable("Chưa cấu hình AiService:ServiceKey (user-secrets).");
        try
        {
            var http = _httpFactory.CreateClient(ImageHttpClient);
            using var message = new HttpRequestMessage(HttpMethod.Post, "images/generate")
            {
                Content = JsonContent.Create(request, options: AiJson.Options)
            };
            message.Headers.Add(AiProtocol.ServiceKeyHeader, _settings.ServiceKey);
            using var response = await http.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AI service từ chối sinh ảnh {Provider}: HTTP {Status}", request.Provider, (int)response.StatusCode);
                return Unavailable($"AI service từ chối yêu cầu sinh ảnh (HTTP {(int)response.StatusCode}).");
            }
            return await response.Content.ReadFromJsonAsync<ImageGenResponse>(AiJson.Options, cancellationToken)
                   ?? Unavailable("AI service trả phản hồi rỗng.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException or System.Text.Json.JsonException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            _logger.LogWarning("Sinh ảnh {Provider} lỗi/timeout ({Type})", request.Provider, ex.GetType().Name);
            return Unavailable(ex is TaskCanceledException or TimeoutException
                ? "Sinh ảnh quá thời gian cho phép, thử lại sau."
                : "AI service đang tắt hoặc không phản hồi.");
        }
    }

    public async Task<VisionCompareResponse> CompareImagesAsync(VisionCompareRequest request, CancellationToken cancellationToken = default)
    {
        var started = DateTime.UtcNow;
        VisionCompareResponse Unavailable(string message) =>
            new(false, null, [], null, request.Model, null, null, started, DateTime.UtcNow, ResultErrorCodes.AiUnavailable, message);

        if (string.IsNullOrWhiteSpace(_settings.ServiceKey)) return Unavailable("Chưa cấu hình AiService:ServiceKey (user-secrets).");
        try
        {
            // Dùng chung HttpClient sinh ảnh (timeout dài, không retry): ảnh base64 + model vision chậm hơn Copilot text
            var http = _httpFactory.CreateClient(ImageHttpClient);
            using var message = new HttpRequestMessage(HttpMethod.Post, "vision/compare")
            {
                Content = JsonContent.Create(request, options: AiJson.Options)
            };
            message.Headers.Add(AiProtocol.ServiceKeyHeader, _settings.ServiceKey);
            using var response = await http.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AI service từ chối vision: HTTP {Status}", (int)response.StatusCode);
                return Unavailable($"AI service từ chối yêu cầu kiểm ảnh (HTTP {(int)response.StatusCode}).");
            }
            return await response.Content.ReadFromJsonAsync<VisionCompareResponse>(AiJson.Options, cancellationToken)
                   ?? Unavailable("AI service trả phản hồi rỗng.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException or System.Text.Json.JsonException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            _logger.LogWarning("Vision lỗi/timeout ({Type})", ex.GetType().Name);
            return Unavailable(ex is TaskCanceledException or TimeoutException
                ? "Kiểm ảnh quá thời gian cho phép, thử lại sau."
                : "AI service đang tắt hoặc không phản hồi.");
        }
    }
}
