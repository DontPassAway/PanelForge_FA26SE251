using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Payments.Models;
using PanelForge.Domain.Entities.Payments;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Payments.Commands.SubscribeVip;

public sealed record SubscribeVipCommand(
    Guid UserId,
    SubscribeVipRequest Request
) : IRequest<Result<SubscribeVipResponse>>;

public sealed class SubscribeVipCommandHandler : IRequestHandler<SubscribeVipCommand, Result<SubscribeVipResponse>>
{
    private readonly IPanelForgeDbContext _db;

    public SubscribeVipCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<SubscribeVipResponse>> Handle(SubscribeVipCommand command, CancellationToken ct)
    {
        var plan = command.Request.Plan;
        int costVnd = VipPricing.CostVnd(plan);

        var wallet = await _db.ReaderWallets
            .FirstOrDefaultAsync(w => w.UserId == command.UserId && !w.IsDeleted, ct);

        if (wallet == null)
        {
            wallet = ReaderWallet.Create(command.UserId, 0m);
            _db.ReaderWallets.Add(wallet);
        }

        if (wallet.BalanceVnd < costVnd)
        {
            int missing = costVnd - (int)wallet.BalanceVnd;
            return Result<SubscribeVipResponse>.Failure(
                $"Số dư Ví không đủ để kích hoạt gói VIP ({wallet.BalanceVnd:N0}đ / {costVnd:N0}đ - thiếu {missing:N0}đ). Vui lòng nạp thêm.",
                ResultErrorCodes.Validation
            );
        }

        wallet.DeductBalance(costVnd);
        var now = DateTime.UtcNow;
        var start = wallet.IsVip && wallet.VipExpiresAt > now ? wallet.VipExpiresAt.Value : now;
        var expiresAt = start.Add(VipPricing.DurationForAmount(costVnd));

        wallet.SetVip(true, expiresAt);

        string orderCode = $"PF{Random.Shared.Next(100000, 999999)}";
        var transaction = PaymentTransaction.Create(
            userId: command.UserId,
            orderCode: orderCode,
            type: PaymentTransactionType.VipSubscription,
            amountVnd: costVnd,
            qrImageUrl: "",
            bankBin: "970422",
            bankAccountNo: "0123456789",
            bankAccountName: "KOMAFORGE STUDIO"
        );
        transaction.Approve(command.UserId, DateTime.UtcNow);
        _db.PaymentTransactions.Add(transaction);

        await _db.SaveChangesAsync(ct);

        var remainingBalance = (int)wallet.BalanceVnd;
        return Result<SubscribeVipResponse>.Success(new SubscribeVipResponse(
            Success: true,
            IsVip: true,
            VipExpiresAt: expiresAt,
            RemainingCoins: remainingBalance / 100,
            Message: "Nâng cấp VIP Pass thành công! Bạn có thể tải CBZ và đọc toàn bộ kho truyện."
        ));
    }
}
