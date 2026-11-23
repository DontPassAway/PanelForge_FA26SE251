using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PanelForge.Application.Common.Interfaces.Services;

namespace PanelForge.Infrastructure.ExternalServices.GooglePlay;

/// <summary>
/// Xác thực giao dịch In-App Product qua Google Play Developer API v3:
/// <c>GET applications/{packageName}/purchases/products/{productId}/tokens/{token}</c>.
/// Cần service account có quyền "View financial data" trong Play Console, cấu hình ở
/// <c>GooglePlay:ServiceAccountKeyPath</c>. Chưa cấu hình → trả NotConfigured (không bao giờ cho qua).
/// </summary>
public sealed class GooglePlayPurchaseVerifier : IGooglePlayPurchaseVerifier
{
    public const string HttpClientName = "GooglePlayDeveloperApi";
    private const string Scope = "https://www.googleapis.com/auth/androidpublisher";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GooglePlayPurchaseVerifier> _logger;
    private readonly Lazy<ITokenAccess?> _credential;

    public GooglePlayPurchaseVerifier(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<GooglePlayPurchaseVerifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        var keyPath = configuration["GooglePlay:ServiceAccountKeyPath"];
        _credential = new Lazy<ITokenAccess?>(() => LoadCredential(keyPath));
    }

    public async Task<GooglePlayPurchaseCheck> VerifyProductPurchaseAsync(
        string packageName,
        string productId,
        string purchaseToken,
        CancellationToken cancellationToken = default)
    {
        var credential = _credential.Value;
        if (credential is null)
            return new GooglePlayPurchaseCheck(
                GooglePlayPurchaseStatus.NotConfigured,
                ErrorMessage: "Máy chủ chưa cấu hình xác thực Google Play (GooglePlay:ServiceAccountKeyPath).");

        var url = "https://androidpublisher.googleapis.com/androidpublisher/v3/applications/"
                  + $"{Uri.EscapeDataString(packageName)}/purchases/products/{Uri.EscapeDataString(productId)}"
                  + $"/tokens/{Uri.EscapeDataString(purchaseToken)}";

        try
        {
            var accessToken = await credential.GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, cancellationToken);

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest or HttpStatusCode.Gone)
                return new GooglePlayPurchaseCheck(
                    GooglePlayPurchaseStatus.Invalid,
                    ErrorMessage: "Google Play không tìm thấy giao dịch này.");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Google Play API trả {Status} khi xác thực {ProductId}", (int)response.StatusCode, productId);
                return new GooglePlayPurchaseCheck(
                    GooglePlayPurchaseStatus.Unavailable,
                    ErrorMessage: "Chưa xác thực được với Google Play, vui lòng thử lại sau.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return ParseProductPurchase(json.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TokenResponseException)
        {
            _logger.LogWarning(ex, "Không gọi được Google Play API khi xác thực {ProductId}", productId);
            return new GooglePlayPurchaseCheck(
                GooglePlayPurchaseStatus.Unavailable,
                ErrorMessage: "Chưa xác thực được với Google Play, vui lòng thử lại sau.");
        }
    }

    /// <summary>Đọc resource ProductPurchase: purchaseState 0 = đã mua, 1 = đã hủy, 2 = đang chờ.</summary>
    public static GooglePlayPurchaseCheck ParseProductPurchase(JsonElement root)
    {
        var state = root.TryGetProperty("purchaseState", out var s) && s.ValueKind == JsonValueKind.Number
            ? s.GetInt32()
            : -1;
        var orderId = root.TryGetProperty("orderId", out var o) ? o.GetString() : null;

        return state switch
        {
            0 => new GooglePlayPurchaseCheck(GooglePlayPurchaseStatus.Purchased, orderId),
            1 => new GooglePlayPurchaseCheck(GooglePlayPurchaseStatus.Canceled, orderId,
                "Giao dịch Google Play đã bị hủy hoặc hoàn tiền."),
            2 => new GooglePlayPurchaseCheck(GooglePlayPurchaseStatus.Pending, orderId,
                "Giao dịch Google Play đang chờ thanh toán."),
            _ => new GooglePlayPurchaseCheck(GooglePlayPurchaseStatus.Invalid, orderId,
                "Phản hồi Google Play không hợp lệ."),
        };
    }

    private ITokenAccess? LoadCredential(string? keyPath)
    {
        if (string.IsNullOrWhiteSpace(keyPath)) return null;
        var fullPath = Path.IsPathRooted(keyPath) ? keyPath : Path.Combine(AppContext.BaseDirectory, keyPath);
        if (!File.Exists(fullPath)) fullPath = Path.GetFullPath(keyPath);
        if (!File.Exists(fullPath))
        {
            _logger.LogWarning("Không tìm thấy file service account Google Play tại {Path}", keyPath);
            return null;
        }

#pragma warning disable CS0618 // CredentialFactory mới chưa có ở bản Google.Apis.Auth đi kèm FirebaseAdmin 3.1
        return GoogleCredential.FromFile(fullPath).CreateScoped(Scope);
#pragma warning restore CS0618
    }
}
