namespace PanelForge.Domain.Enums;

/// <summary>
/// Trạng thái của giao dịch thanh toán VietQR.
/// </summary>
public enum PaymentTransactionStatus
{
    /// <summary>Chờ chuyển khoản / chờ xác nhận.</summary>
    Pending = 1,

    /// <summary>Đã duyệt / thanh toán thành công.</summary>
    Approved = 2,

    /// <summary>Bị từ chối (sai thông tin, không nhận được tiền...).</summary>
    Rejected = 3,

    /// <summary>Đã hết hạn thanh toán.</summary>
    Expired = 4
}

