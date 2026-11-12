using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Payments;

/// <summary>
/// Giao dịch thanh toán chuyển khoản qua VietQR.
/// </summary>
public class PaymentTransaction : BaseEntity
{
    public Guid UserId { get; private set; }
    public Guid? WorkspaceId { get; private set; }
    public Guid? SeriesId { get; private set; }
    public Guid? ChapterId { get; private set; }

    /// <summary>Mã đơn hàng / nội dung chuyển khoản độc nhất (vd: PF123456).</summary>
    public string OrderCode { get; private set; } = default!;

    public PaymentTransactionType Type { get; private set; }
    public decimal AmountVnd { get; private set; }
    public PaymentTransactionStatus Status { get; private set; } = PaymentTransactionStatus.Pending;

    /// <summary>Đường dẫn ảnh QR Code động sinh từ img.vietqr.io.</summary>
    public string QrImageUrl { get; private set; } = default!;

    public string BankBin { get; private set; } = default!;
    public string BankAccountNo { get; private set; } = default!;
    public string BankAccountName { get; private set; } = default!;

    public DateTime? ConfirmedAt { get; private set; }
    public Guid? ConfirmedByUserId { get; private set; }
    public string? RejectionReason { get; private set; }

    public User User { get; private set; } = default!;
    public StudioWorkspace? Workspace { get; private set; }
    public Series? Series { get; private set; }
    public Chapter? Chapter { get; private set; }
    public User? ConfirmedByUser { get; private set; }

    private PaymentTransaction() { }

    public static PaymentTransaction Create(
        Guid userId,
        string orderCode,
        PaymentTransactionType type,
        decimal amountVnd,
        string qrImageUrl,
        string bankBin,
        string bankAccountNo,
        string bankAccountName,
        Guid? workspaceId = null,
        Guid? seriesId = null,
        Guid? chapterId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderCode);
        // qrImageUrl được phép rỗng: giao dịch trừ thẳng từ số dư Ví (mở chương / VIP) không có mã VietQR.
        ArgumentException.ThrowIfNullOrWhiteSpace(bankBin);
        ArgumentException.ThrowIfNullOrWhiteSpace(bankAccountNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(bankAccountName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountVnd);

        return new PaymentTransaction
        {
            UserId = userId,
            OrderCode = orderCode.Trim().ToUpperInvariant(),
            Type = type,
            AmountVnd = amountVnd,
            Status = PaymentTransactionStatus.Pending,
            QrImageUrl = qrImageUrl?.Trim() ?? string.Empty,
            BankBin = bankBin.Trim(),
            BankAccountNo = bankAccountNo.Trim(),
            BankAccountName = bankAccountName.Trim(),
            WorkspaceId = workspaceId,
            SeriesId = seriesId,
            ChapterId = chapterId
        };
    }

    public void Approve(Guid confirmedByUserId, DateTime? confirmedAt = null)
    {
        if (Status != PaymentTransactionStatus.Pending)
            throw new InvalidOperationException($"Chỉ có thể duyệt giao dịch ở trạng thái Pending, hiện là {Status}.");

        Status = PaymentTransactionStatus.Approved;
        ConfirmedByUserId = confirmedByUserId;
        ConfirmedAt = confirmedAt ?? DateTime.UtcNow;
        RejectionReason = null;
    }

    public void Reject(Guid confirmedByUserId, string? reason = null)
    {
        if (Status != PaymentTransactionStatus.Pending)
            throw new InvalidOperationException($"Chỉ có thể từ chối giao dịch ở trạng thái Pending, hiện là {Status}.");

        Status = PaymentTransactionStatus.Rejected;
        ConfirmedByUserId = confirmedByUserId;
        ConfirmedAt = DateTime.UtcNow;
        RejectionReason = reason?.Trim();
    }

    public void Expire()
    {
        if (Status == PaymentTransactionStatus.Pending)
        {
            Status = PaymentTransactionStatus.Expired;
        }
    }
}

