using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Payments.Models;
using PanelForge.Domain.Entities.Payments;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Payments.Commands.ApprovePayment;

public sealed record ApprovePaymentCommand(
    Guid WorkspaceId,
    Guid TransactionId,
    Guid ProducerUserId
) : IRequest<Result<string>>;

public sealed class ApprovePaymentCommandHandler : IRequestHandler<ApprovePaymentCommand, Result<string>>
{
    private readonly IPanelForgeDbContext _db;

    public ApprovePaymentCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<string>> Handle(ApprovePaymentCommand command, CancellationToken ct)
    {
        var tx = await _db.PaymentTransactions
            .FirstOrDefaultAsync(t => t.Id == command.TransactionId && (t.WorkspaceId == command.WorkspaceId || t.WorkspaceId == null) && !t.IsDeleted, ct);

        if (tx == null)
            return Result<string>.Failure("Không tìm thấy giao dịch thanh toán trong Workspace này.", ResultErrorCodes.NotFound);

        if (tx.Status != PaymentTransactionStatus.Pending)
            return Result<string>.Failure($"Giao dịch đã ở trạng thái {tx.Status}, không thể duyệt.", ResultErrorCodes.Validation);

        tx.Approve(command.ProducerUserId, DateTime.UtcNow);

        // Xử lý quyền lợi tương ứng với từng loại giao dịch
        if (tx.Type == PaymentTransactionType.Topup)
        {
            var wallet = await _db.ReaderWallets.FirstOrDefaultAsync(w => w.UserId == tx.UserId && !w.IsDeleted, ct);
            if (wallet == null)
            {
                wallet = ReaderWallet.Create(tx.UserId, 0m);
                _db.ReaderWallets.Add(wallet);
            }
            wallet.AddBalance(tx.AmountVnd);
        }
        else if (tx.Type == PaymentTransactionType.ChapterUnlock && tx.ChapterId.HasValue)
        {
            var exists = await _db.ChapterEntitlements
                .AnyAsync(e => e.UserId == tx.UserId && e.ChapterId == tx.ChapterId.Value
                               && e.Language == (tx.Language ?? ContentLanguages.Original) && !e.IsDeleted, ct);

            if (!exists)
            {
                var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == tx.ChapterId.Value, ct);
                var seriesId = tx.SeriesId ?? chapter?.SeriesId ?? Guid.Empty;

                var entitlement = ChapterEntitlement.Create(
                    userId: tx.UserId,
                    chapterId: tx.ChapterId.Value,
                    seriesId: seriesId,
                    workspaceId: command.WorkspaceId,
                    pricePaidVnd: tx.AmountVnd,
                    transactionId: tx.Id,
                    language: tx.Language
                );
                _db.ChapterEntitlements.Add(entitlement);
            }
        }
        else if (tx.Type == PaymentTransactionType.VipSubscription)
        {
            var wallet = await _db.ReaderWallets.FirstOrDefaultAsync(w => w.UserId == tx.UserId && !w.IsDeleted, ct);
            if (wallet == null)
            {
                wallet = ReaderWallet.Create(tx.UserId, 0m);
                _db.ReaderWallets.Add(wallet);
            }
            // Gia hạn tiếp từ hạn VIP hiện tại (nếu còn), thời hạn theo số tiền gói (tháng / năm).
            var now = DateTime.UtcNow;
            var start = wallet.IsVip && wallet.VipExpiresAt > now ? wallet.VipExpiresAt.Value : now;
            wallet.SetVip(true, start.Add(VipPricing.DurationForAmount(tx.AmountVnd)));
        }

        await _db.SaveChangesAsync(ct);
        return Result<string>.Success($"Đã duyệt thành công giao dịch {tx.OrderCode}.");
    }
}
