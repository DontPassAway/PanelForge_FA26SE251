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
    private readonly HttpClient _http;
    private readonly AiAgentSettings _settings;
    private readonly ILogger<AiClient> _logger;

    public AiClient(HttpClient http, AiAgentSettings settings, ILogger<AiClient> logger)
    {
        _http = http;
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
}