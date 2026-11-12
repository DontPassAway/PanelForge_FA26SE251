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

        var recentTransactions = await _db.PaymentTransactions
            .AsNoTracking()
            .Where(t => t.UserId == q.UserId && !t.IsDeleted)
            .OrderByDescending(t => t.CreatedAt)
            .Take(50)
            .Select(t => new
            {
                t.Id,
                t.Type,
                t.OrderCode,
                t.AmountVnd,
                t.Status,
                t.CreatedAt,
                SeriesTitle = t.Series != null ? t.Series.Title : null,
                ChapterTitle = t.Chapter != null ? t.Chapter.Title : null,
                ChapterNumber = t.Chapter != null ? (decimal?)t.Chapter.ChapterNumber : null
            })
            .ToListAsync(ct);

        var txDtos = recentTransactions.Select(t =>
        {
            string title = t.Type switch
            {
                PaymentTransactionType.Topup => $"Nạp tiền Ví qua VietQR ({t.OrderCode})",
                PaymentTransactionType.ChapterUnlock => t.ChapterNumber.HasValue
                    ? $"Mở khóa Tập {t.ChapterNumber} - {t.SeriesTitle ?? "Truyện"}"
                    : $"Mở khóa chương truyện ({t.OrderCode})",
                PaymentTransactionType.VipSubscription => $"Đăng ký gói đọc truyện VIP ({t.OrderCode})",
                _ => $"Giao dịch {t.OrderCode}"
            };

            int signAmount = (t.Type == PaymentTransactionType.Topup ? (int)t.AmountVnd : -(int)t.AmountVnd);

            return new WalletTransactionDto(
                Id: t.Id.ToString(),
                Title: title,
                AmountVnd: signAmount,
                CreatedAt: t.CreatedAt,
                BalanceAfter: (int)wallet.BalanceVnd
            );
        }).ToList();

        var balanceVndInt = (int)wallet.BalanceVnd;
        return Result<UserWalletDto>.Success(new UserWalletDto(
            BalanceVnd: balanceVndInt,
            BalanceCoins: balanceVndInt / 100,
            IsVip: wallet.IsVip && (wallet.VipExpiresAt == null || wallet.VipExpiresAt > DateTime.UtcNow),
            VipExpiresAt: wallet.VipExpiresAt,
            UnlockedChapterIds: unlockedChapterIds,
            Transactions: txDtos
        ));
    }
}

