using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Domain.Entities.Payments;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Payments.Commands.AdminRefundWallet;

public sealed record AdminRefundWalletRequest(
    Guid TargetUserId,
    int AmountVnd,
    string Reason,
    string? ReferenceOrderCode = null);

public sealed record AdminRefundWalletResponse(
    string RefundOrderCode,
    int NewBalanceVnd,
    string Message);

public sealed record AdminRefundWalletCommand(
    Guid AdminUserId,
    Guid TargetUserId,
    int AmountVnd,
    string Reason,
    string? ReferenceOrderCode = null)
    : IRequest<Result<AdminRefundWalletResponse>>;

public sealed class AdminRefundWalletCommandHandler
    : IRequestHandler<AdminRefundWalletCommand, Result<AdminRefundWalletResponse>>
{
    private readonly IPanelForgeDbContext _db;

    public AdminRefundWalletCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<AdminRefundWalletResponse>> Handle(AdminRefundWalletCommand command, CancellationToken ct)
    {
        if (command.AmountVnd <= 0)
            return Result<AdminRefundWalletResponse>.Failure("Số tiền hoàn phải lớn hơn 0 VNĐ.", ResultErrorCodes.Validation);

        var reason = command.Reason?.Trim() ?? string.Empty;
        if (reason.Length < 5)
            return Result<AdminRefundWalletResponse>.Failure("Vui lòng ghi rõ lý do hoàn tiền (tối thiểu 5 ký tự).", ResultErrorCodes.Validation);

        var userExists = await _db.Users.AsNoTracking().AnyAsync(u => u.Id == command.TargetUserId, ct);
        if (!userExists)
            return Result<AdminRefundWalletResponse>.Failure("Không tìm thấy người dùng cần hoàn tiền.", ResultErrorCodes.NotFound);

        var wallet = await _db.ReaderWallets
            .FirstOrDefaultAsync(w => w.UserId == command.TargetUserId && !w.IsDeleted, ct);
        if (wallet is null)
        {
            wallet = ReaderWallet.Create(command.TargetUserId, 0m);
            _db.ReaderWallets.Add(wallet);
        }

        var refundCode = $"REF{DateTime.UtcNow:yyyyMMddHHmmss}{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}";
        var tx = PaymentTransaction.Create(
            userId: command.TargetUserId,
            orderCode: refundCode,
            type: PaymentTransactionType.Topup,
            amountVnd: command.AmountVnd,
            qrImageUrl: string.Empty,
            bankBin: "ADMIN_REFUND",
            bankAccountNo: command.ReferenceOrderCode?.Trim() ?? "SYSTEM",
            bankAccountName: "Hoàn tiền bởi Quản trị viên");
        tx.Approve(command.AdminUserId, DateTime.UtcNow);
        _db.PaymentTransactions.Add(tx);

        wallet.AddBalance(command.AmountVnd);

        _db.AuditLogs.Add(AuditLog.Create(
            action: "ADMIN_WALLET_REFUND",
            entityName: "ReaderWallet",
            entityId: wallet.Id.ToString(),
            userId: command.AdminUserId,
            details: $"Admin hoàn {command.AmountVnd:N0} VNĐ cho user {command.TargetUserId} (mã {refundCode}, ref {command.ReferenceOrderCode ?? "N/A"}): {reason}"));

        await _db.SaveChangesAsync(ct);

        return Result<AdminRefundWalletResponse>.Success(
            new AdminRefundWalletResponse(
                RefundOrderCode: refundCode,
                NewBalanceVnd: (int)wallet.BalanceVnd,
                Message: $"Đã hoàn {command.AmountVnd:N0} VNĐ vào Ví người dùng (mã {refundCode})."));
    }
}
