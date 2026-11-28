using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Payments.Common;
using PanelForge.Application.Features.Payments.Models;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Domain.Entities.Payments;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Payments.Commands.PurchaseStudioPackage;

/// <summary>
/// D11: độc giả mua gói mở Studio một lần bằng số dư Ví → được <c>CanCreateStudio</c> vĩnh viễn (BR-22).
/// Trừ đúng giá hiện hành do Admin đặt, không tin giá client; giá client thấy khác giá hiện hành → 409.
/// Giao dịch <see cref="PaymentTransactionType.StudioPackage"/> là doanh thu nền tảng nên không gắn Workspace/Series.
/// </summary>
public sealed record PurchaseStudioPackageCommand(Guid UserId, int ExpectedPriceVnd)
    : IRequest<Result<PurchaseStudioPackageResponse>>;

public sealed class PurchaseStudioPackageCommandHandler
    : IRequestHandler<PurchaseStudioPackageCommand, Result<PurchaseStudioPackageResponse>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly DefaultBankSettings _defaultBank;

    public PurchaseStudioPackageCommandHandler(IPanelForgeDbContext db, DefaultBankSettings defaultBank)
    {
        _db = db;
        _defaultBank = defaultBank;
    }

    public async Task<Result<PurchaseStudioPackageResponse>> Handle(PurchaseStudioPackageCommand command, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, ct);
        if (user is null || !user.IsActive)
            return Failure("Người dùng không tồn tại hoặc đã bị vô hiệu hóa.", ResultErrorCodes.Unauthorized);

        // BR-07: Admin không giữ Studio; Publisher là tài khoản phát hành duy nhất (Q12), không mở Studio.
        if (user.Role != SystemRole.User)
            return Failure("Tài khoản Admin / Publisher không mua được gói mở Studio.", ResultErrorCodes.Forbidden);

        if (user.CanCreateStudio)
            return Failure("Tài khoản đã có quyền tạo Studio.", ResultErrorCodes.AlreadyOwned);

        var price = await StudioPackage.CurrentPriceAsync(_db, ct);
        if (price is null)
            return Failure("Gói mở Studio chưa được mở bán.", ResultErrorCodes.NotFound);

        if (command.ExpectedPriceVnd != price.PriceVnd)
            return Failure(
                $"Giá gói đã thay đổi (kỳ vọng {command.ExpectedPriceVnd:N0}đ, hiện tại {price.PriceVnd:N0}đ). Vui lòng tải lại.",
                ResultErrorCodes.PriceChanged);

        var wallet = await _db.ReaderWallets.FirstOrDefaultAsync(w => w.UserId == user.Id && !w.IsDeleted, ct);
        var balance = (int)(wallet?.BalanceVnd ?? 0m);
        if (wallet is null || balance < price.PriceVnd)
            return Failure(
                $"Số dư trong Ví không đủ để mua gói (thiếu {price.PriceVnd - balance:N0}đ). Vui lòng nạp thêm.",
                ResultErrorCodes.Validation);

        wallet.DeductBalance(price.PriceVnd);

        var orderCode = await OrderCodeGenerator.GenerateUniqueAsync(_db, ct);
        var bank = await PlatformBank.CurrentAsync(_db, _defaultBank, ct);
        var transaction = PaymentTransaction.Create(
            userId: user.Id,
            orderCode: orderCode,
            type: PaymentTransactionType.StudioPackage,
            amountVnd: price.PriceVnd,
            qrImageUrl: "",
            bankBin: bank.Bin,
            bankAccountNo: bank.AccountNo,
            bankAccountName: bank.AccountName);
        transaction.Approve(user.Id, DateTime.UtcNow);
        _db.PaymentTransactions.Add(transaction);

        user.GrantStudioCreation();
        _db.AuditLogs.Add(AuditLog.Create(
            action: "STUDIO_PACKAGE_PURCHASED",
            entityName: nameof(PaymentTransaction),
            entityId: transaction.Id.ToString(),
            userId: user.Id,
            details: $"Mua gói mở Studio {price.PriceVnd:N0}đ bằng Ví ({transaction.OrderCode}), cấp quyền tạo Studio."));

        // Ví dùng updated_at làm concurrency token: hai lần mua / mở chương cùng lúc → lần sau nhận 409, không trừ trùng.
        await _db.SaveChangesAsync(ct);

        return Result<PurchaseStudioPackageResponse>.Success(new PurchaseStudioPackageResponse(
            PriceVnd: price.PriceVnd,
            RemainingBalanceVnd: (int)wallet.BalanceVnd,
            OrderCode: transaction.OrderCode,
            CanCreateStudio: true));
    }

    private static Result<PurchaseStudioPackageResponse> Failure(string message, string code) =>
        Result<PurchaseStudioPackageResponse>.Failure(message, code);
}
