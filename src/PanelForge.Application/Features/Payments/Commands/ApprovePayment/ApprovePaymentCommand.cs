using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Payments.Common;
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
        await PaymentFulfillment.GrantAsync(_db, tx, command.WorkspaceId, ct);

        await _db.SaveChangesAsync(ct);
        return Result<string>.Success($"Đã duyệt thành công giao dịch {tx.OrderCode}.");
    }
}
