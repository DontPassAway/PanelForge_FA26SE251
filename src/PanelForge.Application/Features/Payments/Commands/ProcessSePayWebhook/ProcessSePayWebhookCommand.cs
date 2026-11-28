using System.Text.RegularExpressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Payments.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Payments.Commands.ProcessSePayWebhook;

/// <summary>Payload SePay gửi tới webhook khi tài khoản ngân hàng có biến động (https://docs.sepay.vn/tich-hop-webhooks.html).</summary>
public sealed record SePayWebhookPayload(
    long Id,
    string? Gateway,
    string? TransactionDate,
    string? AccountNumber,
    string? Code,
    string? Content,
    string? TransferType,
    long TransferAmount,
    long Accumulated,
    string? SubAccount,
    string? ReferenceCode,
    string? Description
);

/// <summary>Kết quả xử lý: Approved = đã tự duyệt đơn; các giá trị khác là bỏ qua (vẫn trả 200 để SePay không gửi lại).</summary>
public enum SePayWebhookOutcome
{
    Approved,
    IgnoredNotIncoming,
    IgnoredNoOrderCode,
    IgnoredOrderNotFound,
    IgnoredAlreadyProcessed,
    /// <summary>Đơn đã duyệt bởi một lần chuyển khoản khác: độc giả trả hai lần, cần đối soát và hoàn tay.</summary>
    IgnoredPaidTwice,
    IgnoredAccountMismatch,
    IgnoredAmountTooLow
}

/// <summary>
/// Tự duyệt đơn VietQR khi SePay báo có tiền vào: tìm mã đơn PFxxxxxx trong nội dung chuyển khoản,
/// đối chiếu tài khoản nhận và số tiền rồi kích hoạt quyền lợi như khi Producer duyệt tay.
/// Đơn không khớp vẫn ở Pending để Producer xử lý tay trên màn Thanh toán.
/// </summary>
public sealed record ProcessSePayWebhookCommand(SePayWebhookPayload Payload) : IRequest<Result<SePayWebhookOutcome>>;

public sealed partial class ProcessSePayWebhookCommandHandler
    : IRequestHandler<ProcessSePayWebhookCommand, Result<SePayWebhookOutcome>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly ILogger<ProcessSePayWebhookCommandHandler> _logger;

    public ProcessSePayWebhookCommandHandler(IPanelForgeDbContext db, ILogger<ProcessSePayWebhookCommandHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    // Mã đơn do OrderCodeGenerator sinh: PF + 6..12 chữ số (giữ tương thích mã cũ 6 chữ số).
    [GeneratedRegex(@"PF\d{6,12}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OrderCodeRegex();

    public async Task<Result<SePayWebhookOutcome>> Handle(ProcessSePayWebhookCommand command, CancellationToken ct)
    {
        var p = command.Payload;

        if (!string.Equals(p.TransferType, "in", StringComparison.OrdinalIgnoreCase))
            return Done(SePayWebhookOutcome.IgnoredNotIncoming, p);

        var orderCode = ExtractOrderCode(p.Code) ?? ExtractOrderCode(p.Content) ?? ExtractOrderCode(p.Description);
        if (orderCode is null)
            return Done(SePayWebhookOutcome.IgnoredNoOrderCode, p);

        var tx = await _db.PaymentTransactions
            .FirstOrDefaultAsync(t => t.OrderCode == orderCode && !t.IsDeleted, ct);
        if (tx is null)
            return Done(SePayWebhookOutcome.IgnoredOrderNotFound, p, orderCode);

        // OPS-12: SePay gửi lại cùng giao dịch (cùng Id) → bỏ qua. Producer đã duyệt tay (chưa gắn mã giao dịch) → coi
        // chính lần chuyển này là tiền Producer đã thấy. Đơn đã xong bởi giao dịch khác hoặc đã bị từ chối → không cộng,
        // nhưng ghi cảnh báo để đối soát vì tiền thật đã vào tài khoản.
        // Đơn Expired vẫn được duyệt nếu tiền đã vào đúng tài khoản: độc giả đã trả tiền thật.
        var reference = $"sepay:{p.Id}";
        if (tx.Status is not (PaymentTransactionStatus.Pending or PaymentTransactionStatus.Expired))
        {
            if (tx.ProviderReference == reference
                || (tx.Status == PaymentTransactionStatus.Approved && tx.ProviderReference is null))
                return Done(SePayWebhookOutcome.IgnoredAlreadyProcessed, p, orderCode);
            _logger.LogWarning(
                "SePay webhook {SePayId} ({Reference}) trả cho đơn {OrderCode} đã ở trạng thái {Status} (giao dịch {Existing}): cần đối soát, {Amount} VND",
                p.Id, p.ReferenceCode, orderCode, tx.Status, tx.ProviderReference, p.TransferAmount);
            return Result<SePayWebhookOutcome>.Success(SePayWebhookOutcome.IgnoredPaidTwice);
        }

        // Tiền phải vào đúng tài khoản ghi trên QR của đơn (chính hoặc tài khoản ảo/VA).
        if (!SameAccount(p.AccountNumber, tx.BankAccountNo) && !SameAccount(p.SubAccount, tx.BankAccountNo))
            return Done(SePayWebhookOutcome.IgnoredAccountMismatch, p, orderCode);

        if (p.TransferAmount < tx.AmountVnd)
            return Done(SePayWebhookOutcome.IgnoredAmountTooLow, p, orderCode);

        if (tx.Status == PaymentTransactionStatus.Expired) tx.Reopen();
        tx.Approve(confirmedByUserId: null, DateTime.UtcNow, reference);
        await PaymentFulfillment.GrantAsync(_db, tx, tx.WorkspaceId ?? Guid.Empty, ct);
        // Hai lần gửi đến cùng lúc (hoặc Producer duyệt tay đúng lúc này): giao dịch và ví có concurrency token,
        // lần lưu sau ném DbUpdateConcurrencyException → 409 → SePay gửi lại → lần đó gặp IgnoredAlreadyProcessed.
        // provider_reference là khoá duy nhất nên cùng một giao dịch ngân hàng cũng không thể duyệt hai đơn.
        await _db.SaveChangesAsync(ct);

        return Done(SePayWebhookOutcome.Approved, p, orderCode);
    }

    private static string? ExtractOrderCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = OrderCodeRegex().Match(text);
        return m.Success ? m.Value.ToUpperInvariant() : null;
    }

    private static bool SameAccount(string? a, string b) =>
        !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private Result<SePayWebhookOutcome> Done(SePayWebhookOutcome outcome, SePayWebhookPayload p, string? orderCode = null)
    {
        _logger.LogInformation("SePay webhook {SePayId} ({Reference}) order {OrderCode}: {Outcome}",
            p.Id, p.ReferenceCode, orderCode, outcome);
        return Result<SePayWebhookOutcome>.Success(outcome);
    }
}
