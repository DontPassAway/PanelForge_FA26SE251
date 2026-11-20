using System.Net;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;

namespace PanelForge.Infrastructure.Services;

/// <summary>Kiểm tra API key với Google khi Admin lưu cấu hình (UC-02). Hệ thống hiện chỉ chạy Gemini.</summary>
public class AiProviderValidator : IAiProviderValidator
{
    public const string HttpClientName = "gemini-key-check";
    private const string ModelsUrl = "https://generativelanguage.googleapis.com/v1beta/models?pageSize=1";

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
}
