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
    VipSubscription = 3,

    /// <summary>Admin hoàn tiền vào ví độc giả (OPS-06): cộng số dư nhưng không phải tiền nạp hay doanh thu.</summary>
    Refund = 4,

    /// <summary>Độc giả mua gói mở Studio bằng Ví (D11): cấp CanCreateStudio vĩnh viễn; doanh thu nền tảng, không thuộc Workspace.</summary>
    StudioPackage = 5
}

