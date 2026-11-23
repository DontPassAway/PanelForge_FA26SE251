using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Audit;

namespace PanelForge.Application.Features.Payments.Commands.ReportTransactionIssue;

public sealed record ReportTransactionIssueRequest(string Reason);

public sealed record ReportTransactionIssueCommand(
    Guid UserId,
    string OrderCode,
    string Reason)
    : IRequest<Result<string>>;

public sealed class ReportTransactionIssueCommandHandler
    : IRequestHandler<ReportTransactionIssueCommand, Result<string>>
{
    private readonly IPanelForgeDbContext _db;

    public ReportTransactionIssueCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<string>> Handle(ReportTransactionIssueCommand command, CancellationToken ct)
    {
        var reason = command.Reason?.Trim() ?? string.Empty;
        if (reason.Length < 5)
            return Result<string>.Failure("Vui lòng mô tả chi tiết sự cố giao dịch (tối thiểu 5 ký tự).", ResultErrorCodes.Validation);

        var normalizedCode = command.OrderCode.Trim().ToUpperInvariant();
        var tx = await _db.PaymentTransactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.OrderCode == normalizedCode && t.UserId == command.UserId && !t.IsDeleted, ct);

        if (tx is null)
            return Result<string>.Failure("Không tìm thấy giao dịch thuộc tài khoản của bạn.", ResultErrorCodes.NotFound);

        _db.AuditLogs.Add(AuditLog.Create(
            action: "PAYMENT_DISPUTE",
            entityName: "PaymentTransaction",
            entityId: tx.Id.ToString(),
            userId: command.UserId,
            details: $"Báo lỗi giao dịch {tx.OrderCode} ({tx.AmountVnd:N0} VNĐ, trạng thái {tx.Status}): {reason}"));

        await _db.SaveChangesAsync(ct);
        return Result<string>.Success($"Đã tiếp nhận yêu cầu đối soát cho đơn {tx.OrderCode}. Bộ phận hỗ trợ sẽ kiểm tra sớm nhất.");
    }
}
