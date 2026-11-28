using PanelForge.Application.Common.Models;

namespace PanelForge.Application.Features.Payments.Models;

// ─── DTOs cho Mobile Wallet ──────────────────────────────────────────────────

public sealed record WalletTransactionDto(
    string Id,
    string Title,
    int AmountVnd,
    DateTime CreatedAt,
    int BalanceAfter,
    string OrderCode = ""
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
/// "Topup" (mặc định, nạp ví) | "ChapterUnlock" (trả VietQR trực tiếp để mở chương — cần ChapterId, số tiền do server quyết định).
/// Gói VIP đã bỏ (Q4): "VipSubscription" bị từ chối; <c>VipPlan</c> giữ lại cho client cũ và bị bỏ qua.
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

public sealed record UnlockChapterRequest(
    Guid ChapterId,
    int? PriceVnd = null,
    Guid? SeriesId = null,
    bool IsPermanent = true,
    int? RentalViews = null,
    string? Language = null,
    int? ExpectedPriceVnd = null
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
    int PaidTransactionsCount,
    decimal TotalTopupsVnd = 0m,
    decimal TotalUnlockRevenueVnd = 0m
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

