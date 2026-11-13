using PanelForge.Application.Common.Models;

namespace PanelForge.Application.Features.Payments.Models;

// ─── DTOs cho Mobile Wallet ──────────────────────────────────────────────────

public sealed record WalletTransactionDto(
    string Id,
    string Title,
    int AmountVnd,
    DateTime CreatedAt,
    int BalanceAfter
);

public sealed record UserWalletDto(
    int BalanceVnd,
    int BalanceCoins,
    bool IsVip,
    DateTime? VipExpiresAt,
    IReadOnlyList<string> UnlockedChapterIds,
    IReadOnlyList<WalletTransactionDto> Transactions
);

/// <param name="Purpose">
/// "Topup" (mặc định, nạp ví) | "ChapterUnlock" (trả VietQR trực tiếp để mở chương — cần ChapterId)
/// | "VipSubscription" (trả VietQR trực tiếp gói VIP — dùng VipPlan). Với ChapterUnlock/VIP số tiền do server quyết định.
/// </param>
public sealed record TopupRequest(
    int? CoinsAmount = null,
    int? AmountVnd = null,
    string? PaymentMethod = "vietqr",
    Guid? WorkspaceId = null,
    Guid? SeriesId = null,
    Guid? ChapterId = null,
    string? Purpose = null,
    string? Language = null,
    string? VipPlan = null
);

public sealed record TopupResponse(
    string OrderId,
    int Coins,
    int AmountVnd,
    string? QrCodeUrl,
    string? BankName,
    string? AccountNumber,
    string? AccountName,
    string? TransferContent,
    bool IsCompleted,
    string Purpose = "Topup",
    string Status = "Pending"
);

/// <summary>Bảng giá & thời hạn gói VIP — dùng chung cho thanh toán bằng Ví và VietQR trực tiếp.</summary>
public static class VipPricing
{
    public const int MonthlyVnd = 19900;
    public const int YearlyVnd = 199900;

    public static bool IsYearly(string? plan) =>
        string.Equals(plan?.Trim(), "yearly", StringComparison.OrdinalIgnoreCase);

    public static int CostVnd(string? plan) => IsYearly(plan) ? YearlyVnd : MonthlyVnd;

    /// <summary>Thời hạn theo số tiền đã trả (giao dịch VietQR chỉ lưu AmountVnd).</summary>
    public static TimeSpan DurationForAmount(decimal amountVnd) =>
        amountVnd >= YearlyVnd ? TimeSpan.FromDays(365) : TimeSpan.FromDays(30);
}

public sealed record UnlockChapterRequest(
    Guid ChapterId,
    int? PriceVnd = null,
    Guid? SeriesId = null,
    bool IsPermanent = true,
    int? RentalViews = null,
    string? Language = null
);

public sealed record UnlockChapterResponse(
    string ChapterId,
    bool Success,
    int RemainingCoins,
    int RemainingBalanceVnd,
    string? CbzDownloadUrl = null,
    string? Message = null,
    bool IsPermanent = true,
    int? RemainingViews = null
);

public sealed record SubscribeVipRequest(
    string Plan = "monthly"
);

public sealed record SubscribeVipResponse(
    bool Success,
    bool IsVip,
    DateTime? VipExpiresAt,
    int RemainingCoins,
    string? Message
);

public sealed record TransactionStatusDto(
    string OrderCode,
    string Status,
    bool IsCompleted,
    decimal AmountVnd,
    DateTime? ConfirmedAt,
    string? RejectionReason
);

// ─── DTOs cho Producer Workspace Payments ────────────────────────────────────

public sealed record WorkspacePaymentsSummaryDto(
    decimal TotalRevenueVnd,
    int PendingCount,
    int TotalUnlockedChapters,
    int PaidTransactionsCount
);

public sealed record WorkspacePaymentItemDto(
    Guid Id,
    string OrderCode,
    string ReaderName,
    string ReaderEmail,
    string? ReaderAvatarUrl,
    string Type,
    Guid? SeriesId,
    string? SeriesTitle,
    Guid? ChapterId,
    decimal? ChapterNumber,
    string? ChapterTitle,
    decimal AmountVnd,
    string Status,
    string QrImageUrl,
    DateTime CreatedAt,
    DateTime? ConfirmedAt,
    string? ConfirmedByName,
    string? RejectionReason
);

public sealed record WorkspacePaymentsResponseDto(
    WorkspacePaymentsSummaryDto Summary,
    PagedResult<WorkspacePaymentItemDto> Transactions
);

public sealed record RejectPaymentRequest(
    string? Reason = null
);

public sealed record WorkspaceBankSettingsDto(
    Guid WorkspaceId,
    string? BankBin,
    string? BankName,
    string? BankAccountNo,
    string? BankAccountName
);

public sealed record UpdateWorkspaceBankSettingsRequest(
    string? BankBin,
    string? BankName,
    string? BankAccountNo,
    string? BankAccountName
);

