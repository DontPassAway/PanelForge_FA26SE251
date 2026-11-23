namespace PanelForge.Application.Common.Interfaces.Services;

/// <summary>
/// Hỏi Google Play Developer API (purchases.products.get) xem một purchaseToken có phải giao dịch thật,
/// đã thanh toán, đúng gói và đúng ứng dụng hay không. Không bao giờ tin dữ liệu client gửi lên.
/// </summary>
public interface IGooglePlayPurchaseVerifier
{
    Task<GooglePlayPurchaseCheck> VerifyProductPurchaseAsync(
        string packageName,
        string productId,
        string purchaseToken,
        CancellationToken cancellationToken = default);
}

public enum GooglePlayPurchaseStatus
{
    /// <summary>Google xác nhận đã thanh toán (purchaseState = 0).</summary>
    Purchased,
    /// <summary>Giao dịch đang chờ thanh toán (purchaseState = 2) — chưa được cộng tiền.</summary>
    Pending,
    /// <summary>Giao dịch đã hủy / hoàn tiền (purchaseState = 1).</summary>
    Canceled,
    /// <summary>Token không tồn tại / không thuộc gói hoặc ứng dụng này.</summary>
    Invalid,
    /// <summary>Máy chủ chưa cấu hình service account Google Play.</summary>
    NotConfigured,
    /// <summary>Không gọi được Google (lỗi mạng / 5xx) — client nên thử lại sau.</summary>
    Unavailable,
}

public sealed record GooglePlayPurchaseCheck(
    GooglePlayPurchaseStatus Status,
    string? GoogleOrderId = null,
    string? ErrorMessage = null);
