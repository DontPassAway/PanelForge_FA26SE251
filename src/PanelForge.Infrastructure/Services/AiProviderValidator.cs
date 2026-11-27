using System.Net;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiConfig.Models;

namespace PanelForge.Infrastructure.Services;

/// <summary>Kiểm tra API key với Google khi Admin lưu cấu hình (UC-02). Hệ thống hiện chỉ chạy Gemini.</summary>
public class AiProviderValidator : IAiProviderValidator
{
    public const string HttpClientName = "gemini-key-check";
    private const string ModelsUrl = "https://generativelanguage.googleapis.com/v1beta/models?pageSize=1";
    private const string CloudflareAccountsUrl = "https://api.cloudflare.com/client/v4/accounts/";

    private readonly IHttpClientFactory _httpFactory;

    public AiProviderValidator(IHttpClientFactory httpFactory) => _httpFactory = httpFactory;

    public Task<Result<bool>> ValidateCredentialsAsync(
        string provider, string apiKey, CancellationToken cancellationToken = default) =>
        CheckGeminiAsync(provider, apiKey, allowRateLimited: true, cancellationToken);

    public Task<Result<bool>> ValidateCloudflareAsync(
        string accountId, string apiToken, CancellationToken cancellationToken = default) =>
        CheckCloudflareAsync(accountId, apiToken, allowRateLimited: true, cancellationToken);

    public Task<Result<bool>> ProbeAsync(
        string providerType, string? accountId, string apiKey, CancellationToken cancellationToken = default) =>
        string.Equals(providerType, PanelForge.Domain.Entities.Ai.AiProviderCredential.Cloudflare, StringComparison.OrdinalIgnoreCase)
            ? CheckCloudflareAsync(accountId ?? "", apiKey, allowRateLimited: false, cancellationToken)
            : CheckGeminiAsync(providerType, apiKey, allowRateLimited: false, cancellationToken);

    private static bool IsValidHeaderToken(string token) =>
        token.Length > 0 && !token.StartsWith("••••", StringComparison.Ordinal) && token.All(c => c is >= '!' and <= '~');

    private async Task<Result<bool>> CheckGeminiAsync(
        string provider, string apiKey, bool allowRateLimited, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return Result<bool>.Failure("API Key không được để trống.");
        var trimmed = apiKey.Trim();
        if (!IsValidHeaderToken(trimmed))
            return Result<bool>.Failure("API Key chứa ký tự không hợp lệ hoặc là chuỗi đã che (••••). Hãy nhập API key gốc.");
        if (provider?.Contains("Gemini", StringComparison.OrdinalIgnoreCase) != true)
            return Result<bool>.Failure("Hiện hệ thống chỉ hỗ trợ Google Gemini.");

        using var request = new HttpRequestMessage(HttpMethod.Get, ModelsUrl);
        request.Headers.TryAddWithoutValidation("x-goog-api-key", trimmed);
        try
        {
            using var response = await _httpFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return Result<bool>.Failure("Google từ chối API key này. Hãy kiểm tra lại key.");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return allowRateLimited
                    ? Result<bool>.Success(true)
                    : Result<bool>.Failure("API key hợp lệ nhưng đang bị giới hạn tần suất hoặc hết quota (HTTP 429).");
            if ((int)response.StatusCode >= 500)
                return Result<bool>.Failure($"Google đang lỗi máy chủ (HTTP {(int)response.StatusCode}), chưa kiểm tra được API key. Hãy thử lại sau.");
            return response.IsSuccessStatusCode
                ? Result<bool>.Success(true)
                : Result<bool>.Failure($"Google từ chối kiểm tra API key (HTTP {(int)response.StatusCode}).");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Result<bool>.Failure("Không kết nối được Google để kiểm tra API key. Hãy thử lại.");
        }
    }

    private async Task<Result<bool>> CheckCloudflareAsync(
        string accountId, string apiToken, bool allowRateLimited, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(apiToken))
            return Result<bool>.Failure("Cloudflare cần cả Account ID và API token.");
        var trimmedToken = apiToken.Trim();
        if (!IsValidHeaderToken(trimmedToken))
            return Result<bool>.Failure("API token chứa ký tự không hợp lệ hoặc là chuỗi đã che (••••). Hãy nhập token gốc.");

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{CloudflareAccountsUrl}{Uri.EscapeDataString(accountId.Trim())}/ai/models/search?per_page=1");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", trimmedToken);
        try
        {
            using var response = await _httpFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            // 404 = sai Account ID; 400/401/403 = token sai hoặc thiếu quyền Workers AI
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized
                or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                return Result<bool>.Failure("Cloudflare từ chối Account ID/API token này (token cần quyền Workers AI Read).");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return allowRateLimited
                    ? Result<bool>.Success(true)
                    : Result<bool>.Failure("API token hợp lệ nhưng Cloudflare đang giới hạn lượt gọi (HTTP 429).");
            if ((int)response.StatusCode >= 500)
                return Result<bool>.Failure($"Cloudflare đang lỗi máy chủ (HTTP {(int)response.StatusCode}), chưa kiểm tra được token. Hãy thử lại sau.");
            return response.IsSuccessStatusCode
                ? Result<bool>.Success(true)
                : Result<bool>.Failure($"Cloudflare từ chối kiểm tra token (HTTP {(int)response.StatusCode}).");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FormatException)
        {
            return Result<bool>.Failure("Không kết nối được Cloudflare để kiểm tra token. Hãy thử lại.");
        }
    }

    public async Task<Result<IReadOnlyList<AiProviderModelDto>>> ListCloudflareModelsAsync(
        string accountId, string apiToken, CancellationToken cancellationToken = default)
    {
        // ponytail: một trang 500 model là đủ (Workers AI hiện ~100 model); cần thì lặp theo result_info.total_pages
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{CloudflareAccountsUrl}{Uri.EscapeDataString(accountId.Trim())}/ai/models/search?per_page=500");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiToken.Trim());
        try
        {
            using var response = await _httpFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return Result<IReadOnlyList<AiProviderModelDto>>.Failure(
                    $"Cloudflare từ chối lấy danh sách model (HTTP {(int)response.StatusCode}).");

            var json = System.Text.Json.Nodes.JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var models = new List<AiProviderModelDto>();
            foreach (var m in json?["result"]?.AsArray() ?? [])
            {
                var id = m?["name"]?.GetValue<string>();
                var kind = (m?["task"]?["name"]?.GetValue<string>()) switch
                {
                    "Text Generation" => "text",
                    "Text-to-Image" => "image",
                    _ => null
                };
                if (string.IsNullOrWhiteSpace(id) || kind is null) continue;
                var description = m?["description"]?.GetValue<string>();
                models.Add(new AiProviderModelDto(id, kind, id.StartsWith("@cf/", StringComparison.Ordinal),
                    description is { Length: > 200 } ? description[..200] + "…" : description));
            }
            return Result<IReadOnlyList<AiProviderModelDto>>.Success(
                models.OrderBy(m => m.Kind).ThenByDescending(m => m.Free).ThenBy(m => m.Id, StringComparer.Ordinal).ToList());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return Result<IReadOnlyList<AiProviderModelDto>>.Failure("Không lấy được danh sách model từ Cloudflare. Hãy thử lại.");
        }
    }
}
