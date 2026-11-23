using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Payments.Models;
using PanelForge.Domain.Entities.Payments;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Payments.Common;

/// <summary>
/// Kích hoạt quyền lợi của một giao dịch VietQR vừa được duyệt (Producer duyệt tay hoặc webhook SePay):
/// nạp ví, mở chương hoặc gia hạn VIP. Không gọi SaveChanges — để caller lưu cùng lúc với trạng thái giao dịch.
/// </summary>
public static class PaymentFulfillment
{
    public static async Task GrantAsync(IPanelForgeDbContext db, PaymentTransaction tx, Guid workspaceId, CancellationToken ct)
    {
        if (tx.Type == PaymentTransactionType.Topup)
        {
            var wallet = await GetOrCreateWalletAsync(db, tx.UserId, ct);
            wallet.AddBalance(tx.AmountVnd);
        }
        else if (tx.Type == PaymentTransactionType.ChapterUnlock && tx.ChapterId.HasValue)
        {
            var exists = await db.ChapterEntitlements
                .AnyAsync(e => e.UserId == tx.UserId && e.ChapterId == tx.ChapterId.Value
                               && e.Language == (tx.Language ?? ContentLanguages.Original) && !e.IsDeleted, ct);

            if (!exists)
            {
                var chapter = await db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == tx.ChapterId.Value, ct);
                var seriesId = tx.SeriesId ?? chapter?.SeriesId ?? Guid.Empty;

                db.ChapterEntitlements.Add(ChapterEntitlement.Create(
                    userId: tx.UserId,
                    chapterId: tx.ChapterId.Value,
                    seriesId: seriesId,
                    workspaceId: workspaceId,
                    pricePaidVnd: tx.AmountVnd,
                    transactionId: tx.Id,
                    language: tx.Language
                ));
            }
        }
        else if (tx.Type == PaymentTransactionType.VipSubscription)
        {
            var wallet = await GetOrCreateWalletAsync(db, tx.UserId, ct);
            // Gia hạn tiếp từ hạn VIP hiện tại (nếu còn), thời hạn theo số tiền gói (tháng / năm).
            var now = DateTime.UtcNow;
            var start = wallet.IsVip && wallet.VipExpiresAt > now ? wallet.VipExpiresAt.Value : now;
            wallet.SetVip(true, start.Add(VipPricing.DurationForAmount(tx.AmountVnd)));
        }
    }

    private static async Task<ReaderWallet> GetOrCreateWalletAsync(IPanelForgeDbContext db, Guid userId, CancellationToken ct)
    {
        var wallet = await db.ReaderWallets.FirstOrDefaultAsync(w => w.UserId == userId && !w.IsDeleted, ct);
        if (wallet == null)
        {
            wallet = ReaderWallet.Create(userId, 0m);
            db.ReaderWallets.Add(wallet);
        }
        return wallet;
    }
}
