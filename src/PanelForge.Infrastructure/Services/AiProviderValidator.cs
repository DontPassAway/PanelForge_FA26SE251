using System.Net;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;

namespace PanelForge.Infrastructure.Services;

/// <summary>Kiểm tra API key với Google khi Admin lưu cấu hình (UC-02). Hệ thống hiện chỉ chạy Gemini.</summary>
public class AiProviderValidator : IAiProviderValidator
{
    public const string HttpClientName = "gemini-key-check";
    private const string ModelsUrl = "https://generativelanguage.googleapis.com/v1beta/models?pageSize=1";
    private const string CloudflareAccountsUrl = "https://api.cloudflare.com/client/v4/accounts/";

    private readonly IHttpClientFactory _httpFactory;

    public AiProviderValidator(IHttpClientFactory httpFactory) => _httpFactory = httpFactory;

    public async Task<Result<bool>> ValidateCredentialsAsync(
        string provider, string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return Result<bool>.Failure("API Key không được để trống.");
        if (provider?.Contains("Gemini", StringComparison.OrdinalIgnoreCase) != true)
            return Result<bool>.Failure("Hiện hệ thống chỉ hỗ trợ Google Gemini.");

        using var request = new HttpRequestMessage(HttpMethod.Get, ModelsUrl);
        request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey.Trim());
        try
        {
            using var response = await _httpFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            // 429 và lỗi máy chủ không nói lên key sai: vẫn cho lưu
            return response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? Result<bool>.Failure("Google từ chối API key này. Hãy kiểm tra lại key.")
                : Result<bool>.Success(true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Result<bool>.Failure("Không kết nối được Google để kiểm tra API key. Hãy thử lại.");
        }
    }

    public async Task<Result<bool>> ValidateCloudflareAsync(string accountId, string apiToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(apiToken))
            return Result<bool>.Failure("Cloudflare cần cả Account ID và API token.");

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{CloudflareAccountsUrl}{Uri.EscapeDataString(accountId.Trim())}/ai/models/search?per_page=1");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiToken.Trim());
        try
        {
            using var response = await _httpFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            // 404 = sai Account ID; 400/401/403 = token sai hoặc thiếu quyền Workers AI
            return response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized
                    or HttpStatusCode.Forbidden or HttpStatusCode.NotFound
                ? Result<bool>.Failure("Cloudflare từ chối Account ID/API token này (token cần quyền Workers AI Read).")
                : Result<bool>.Success(true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Result<bool>.Failure("Không kết nối được Cloudflare để kiểm tra token. Hãy thử lại.");
        }
    }
}
