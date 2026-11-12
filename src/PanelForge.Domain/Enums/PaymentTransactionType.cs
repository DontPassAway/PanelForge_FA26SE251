namespace PanelForge.Domain.Enums;

/// <summary>
/// Loại giao dịch thanh toán.
/// </summary>
public enum PaymentTransactionType
{
    /// <summary>Nạp tiền vào ví độc giả.</summary>
    Topup = 1,

    /// <summary>Mở khóa chương trả phí.</summary>
    ChapterUnlock = 2,

    /// <summary>Đăng ký gói VIP đọc truyện.</summary>
    VipSubscription = 3
}

