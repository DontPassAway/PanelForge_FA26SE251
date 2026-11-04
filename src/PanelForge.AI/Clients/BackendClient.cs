using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using PanelForge.AI.Contracts;

namespace PanelForge.AI.Clients;

public sealed class BackendOptions
{
    public const string SectionName = "Backend";
    public string BaseUrl { get; set; } = "http://127.0.0.1:5044";
}

public sealed class BackendException(string message, HttpStatusCode? status = null) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>
/// Gọi API nội bộ của Backend chính (context + báo step). Mang khoá dịch vụ; Backend kiểm run/scope — service này
/// không đọc DB và không tự khai báo userId/workspaceId/seriesId. Retry/circuit breaker cấu hình ở Program.cs.
/// </summary>
public sealed class BackendClient
{
    private readonly HttpClient _http;

    public BackendClient(HttpClient http) => _http = http;

    public async Task<AgentContext> GetContextAsync(Guid runId, CancellationToken ct)
    {
        using var response = await _http.GetAsync($"internal/ai/runs/{runId}/context", ct);
        if (!response.IsSuccessStatusCode)
            throw new BackendException($"Backend từ chối trả context (HTTP {(int)response.StatusCode}).", response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AgentContext>(AiJson.Options, ct))
               ?? throw new BackendException("Backend trả context rỗng.");
    }

    /// <summary>Null khi Backend trả 409 (run đã kết thúc / step không đúng thứ tự): workflow phải dừng, không báo tiếp.</summary>
    public async Task<AgentStepAck?> ReportStepAsync(Guid runId, AgentStepReport report, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync($"internal/ai/runs/{runId}/steps", report, AiJson.Options, ct);
        if (response.StatusCode == HttpStatusCode.Conflict) return null;
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new BackendException($"Backend từ chối step (HTTP {(int)response.StatusCode}): {Truncate(body)}", response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<AgentStepAck>(AiJson.Options, ct);
    }

    /// <summary>Bắn delta token thời gian thực sang Backend; chạy fire-and-forget, không làm gián đoạn luồng chính nếu lỗi mạng.</summary>
    public async Task StreamTokenAsync(Guid runId, StreamTokenPayload payload, CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync($"internal/ai/runs/{runId}/stream-token", payload, AiJson.Options, ct);
        }
        catch
        {
            // Best effort streaming
        }
    }

    private static string Truncate(string s) => s.Length <= 400 ? s : s[..400] + "…";
}