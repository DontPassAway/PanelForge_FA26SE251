using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Payments.Commands.RejectPayment;

public sealed record RejectPaymentCommand(
    Guid WorkspaceId,
    Guid TransactionId,
    Guid ProducerUserId,
    string? Reason = null
) : IRequest<Result<string>>;

public sealed class RejectPaymentCommandHandler : IRequestHandler<RejectPaymentCommand, Result<string>>
{
    private readonly IPanelForgeDbContext _db;

    public RejectPaymentCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<string>> Handle(RejectPaymentCommand command, CancellationToken ct)
    {
        var tx = await _db.PaymentTransactions
            .FirstOrDefaultAsync(t => t.Id == command.TransactionId && (t.WorkspaceId == command.WorkspaceId || t.WorkspaceId == null) && !t.IsDeleted, ct);

        if (tx == null)
            return Result<string>.Failure("Không tìm thấy giao dịch thanh toán trong Workspace này.", ResultErrorCodes.NotFound);

        if (tx.Status != PaymentTransactionStatus.Pending)
            return Result<string>.Failure($"Giao dịch đã ở trạng thái {tx.Status}, không thể từ chối.", ResultErrorCodes.Validation);

        tx.Reject(command.ProducerUserId, command.Reason);
        await _db.SaveChangesAsync(ct);

        return Result<string>.Success($"Đã từ chối giao dịch {tx.OrderCode}.");
    }
}
