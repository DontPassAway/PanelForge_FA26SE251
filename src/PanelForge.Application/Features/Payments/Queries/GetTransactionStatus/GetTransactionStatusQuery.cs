using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Payments.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Payments.Queries.GetTransactionStatus;

public sealed record GetTransactionStatusQuery(string OrderCode) : IRequest<Result<TransactionStatusDto>>;

public sealed class GetTransactionStatusQueryHandler : IRequestHandler<GetTransactionStatusQuery, Result<TransactionStatusDto>>
{
    private readonly IPanelForgeDbContext _db;

    public GetTransactionStatusQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<TransactionStatusDto>> Handle(GetTransactionStatusQuery q, CancellationToken ct)
    {
        var tx = await _db.PaymentTransactions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.OrderCode == q.OrderCode.Trim().ToUpperInvariant() && !t.IsDeleted, ct);

        if (tx == null)
            return Result<TransactionStatusDto>.Failure("Không tìm thấy giao dịch.", ResultErrorCodes.NotFound);

        return Result<TransactionStatusDto>.Success(new TransactionStatusDto(
            OrderCode: tx.OrderCode,
            Status: tx.Status.ToString(),
            IsCompleted: tx.Status == PaymentTransactionStatus.Approved,
            AmountVnd: tx.AmountVnd,
            ConfirmedAt: tx.ConfirmedAt,
            RejectionReason: tx.RejectionReason
        ));
    }
}

