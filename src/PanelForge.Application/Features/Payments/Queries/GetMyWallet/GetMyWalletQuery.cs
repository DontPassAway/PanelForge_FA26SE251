using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Payments.Models;
using PanelForge.Domain.Entities.Payments;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Payments.Queries.GetMyWallet;

public sealed record GetMyWalletQuery(Guid UserId) : IRequest<Result<UserWalletDto>>;

public sealed class GetMyWalletQueryHandler : IRequestHandler<GetMyWalletQuery, Result<UserWalletDto>>
{
    private readonly IPanelForgeDbContext _db;

    public GetMyWalletQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<UserWalletDto>> Handle(GetMyWalletQuery q, CancellationToken ct)
    {
        var wallet = await _db.ReaderWallets
            .FirstOrDefaultAsync(w => w.UserId == q.UserId && !w.IsDeleted, ct);

        if (wallet == null)
        {
            wallet = ReaderWallet.Create(q.UserId, 0m);
            _db.ReaderWallets.Add(wallet);
            await _db.SaveChangesAsync(ct);
        }

        var unlockedChapterIds = await _db.ChapterEntitlements
            .AsNoTracking()
            .Where(e => e.UserId == q.UserId && !e.IsDeleted)
            .Select(e => e.ChapterId.ToString())
            .ToListAsync(ct);

        // Lịch sử Ví chỉ gồm giao dịch đã thực sự làm đổi số dư: đơn Approved, là nạp ví (VietQR / Google Play /
        // Admin hoàn tiền) hoặc mở chương / VIP trừ thẳng từ Ví (không có mã VietQR). Đơn Pending/Rejected/Expired
        // và đơn mở chương / VIP trả trực tiếp bằng VietQR không đi qua Ví nên không xuất hiện.
        var recentTransactions = await _db.PaymentTransactions
            .AsNoTracking()
            .Where(t => t.UserId == q.UserId && !t.IsDeleted
                        && t.Status == PaymentTransactionStatus.Approved
                        && (t.Type == PaymentTransactionType.Topup || t.Type == PaymentTransactionType.Refund
                            || t.QrImageUrl == ""))
            .OrderByDescending(t => t.ConfirmedAt ?? t.CreatedAt)
            .ThenByDescending(t => t.CreatedAt)
            .Take(50)
            .Select(t => new
            {
                t.Id,
                t.Type,
                t.OrderCode,
                t.AmountVnd,
                OccurredAt = t.ConfirmedAt ?? t.CreatedAt,
                t.BankBin,
                SeriesTitle = t.Series != null ? t.Series.Title : null,
                ChapterTitle = t.Chapter != null ? t.Chapter.Title : null,
                ChapterNumber = t.Chapter != null ? (decimal?)t.Chapter.ChapterNumber : null
            })
            .ToListAsync(ct);

        // Số dư sau mỗi giao dịch: đi lùi từ số dư hiện tại (giao dịch mới nhất trước).
        var runningBalance = (int)wallet.BalanceVnd;
        var txDtos = new List<WalletTransactionDto>(recentTransactions.Count);
        foreach (var t in recentTransactions)
        {
            string title = t.Type switch
            {
                PaymentTransactionType.Topup => t.BankBin switch
                {
                    "GOOGLE_PLAY" => $"Nạp tiền Ví qua Google Play ({t.OrderCode})",
                    _ => $"Nạp tiền Ví qua VietQR ({t.OrderCode})"
                },
                PaymentTransactionType.Refund => $"Hoàn tiền vào Ví ({t.OrderCode})",
                PaymentTransactionType.ChapterUnlock => t.ChapterNumber.HasValue
                    ? $"Mở khóa Tập {t.ChapterNumber} - {t.SeriesTitle ?? "Truyện"}"
                    : $"Mở khóa chương truyện ({t.OrderCode})",
                PaymentTransactionType.VipSubscription => $"Đăng ký gói đọc truyện VIP ({t.OrderCode})",
                PaymentTransactionType.StudioPackage => $"Mua gói mở Studio ({t.OrderCode})",
                _ => $"Giao dịch {t.OrderCode}"
            };

            int signAmount = t.Type is PaymentTransactionType.Topup or PaymentTransactionType.Refund
                ? (int)t.AmountVnd : -(int)t.AmountVnd;

            txDtos.Add(new WalletTransactionDto(
                Id: t.Id.ToString(),
                Title: title,
                AmountVnd: signAmount,
                CreatedAt: t.OccurredAt,
                BalanceAfter: runningBalance,
                OrderCode: t.OrderCode
            ));
            runningBalance -= signAmount;
        }

        var balanceVndInt = (int)wallet.BalanceVnd;
        return Result<UserWalletDto>.Success(new UserWalletDto(
            BalanceVnd: balanceVndInt,
            BalanceCoins: balanceVndInt / 100,
            // Q4 (OPS-11): VIP không còn quyền đọc; giữ trường cho client cũ nhưng luôn báo không VIP.
            IsVip: false,
            VipExpiresAt: null,
            UnlockedChapterIds: unlockedChapterIds,
            Transactions: txDtos
        ));
    }
}

