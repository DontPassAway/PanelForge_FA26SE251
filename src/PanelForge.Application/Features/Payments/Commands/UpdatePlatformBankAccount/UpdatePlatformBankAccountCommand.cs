using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Authentication;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Auth.Common;
using PanelForge.Application.Features.Payments.Models;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Domain.Entities.Payments;

namespace PanelForge.Application.Features.Payments.Commands.UpdatePlatformBankAccount;

/// <summary>
/// Publisher đổi tài khoản ngân hàng nền tảng nhận tiền VietQR (Q13, BR-32). Đổi số tài khoản là thao tác chuyển hướng
/// dòng tiền nên bắt buộc tài khoản Publisher đã bật 2FA và nhập mã đúng; ghi một bản ghi mới (append-only) và audit log.
/// Đơn Pending đã tạo giữ nguyên tài khoản cũ in trên mã QR.
/// </summary>
public sealed record UpdatePlatformBankAccountCommand(
    Guid UserId,
    string BankBin,
    string BankName,
    string AccountNo,
    string AccountName,
    string TwoFactorCode
) : IRequest<Result<PlatformBankAccountDto>>;

public sealed class UpdatePlatformBankAccountCommandHandler : IRequestHandler<UpdatePlatformBankAccountCommand, Result<PlatformBankAccountDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly ITotpService _totp;

    public UpdatePlatformBankAccountCommandHandler(IPanelForgeDbContext db, ITotpService totp)
    {
        _db = db;
        _totp = totp;
    }

    public async Task<Result<PlatformBankAccountDto>> Handle(UpdatePlatformBankAccountCommand command, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, ct);
        if (user is null || !user.IsActive)
            return Result<PlatformBankAccountDto>.Failure("Người dùng không tồn tại hoặc đã bị vô hiệu hóa.", ResultErrorCodes.Unauthorized);

        if (!user.TwoFactorEnabled)
            return Result<PlatformBankAccountDto>.Failure(
                "Bật xác thực hai lớp (2FA) cho tài khoản trước khi đổi tài khoản nhận tiền.", ResultErrorCodes.Forbidden);

        if (SecondFactor.Verify(user, _totp, command.TwoFactorCode) is SecondFactor.Outcome.Invalid or SecondFactor.Outcome.AlreadyUsed)
            return Result<PlatformBankAccountDto>.Failure(
                "Mã 2FA hoặc mã khôi phục không đúng, đã hết hạn hoặc đã được dùng.", ResultErrorCodes.Validation);

        PlatformBankAccount account;
        try
        {
            account = PlatformBankAccount.Create(command.BankBin, command.BankName, command.AccountNo, command.AccountName, user.Id);
        }
        catch (ArgumentException ex)
        {
            return Result<PlatformBankAccountDto>.Failure(ex.Message.Split(" (Parameter")[0], ResultErrorCodes.Validation);
        }

        var previous = await _db.PlatformBankAccounts.AsNoTracking()
            .Where(a => !a.IsDeleted)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new { a.BankName, a.AccountNo })
            .FirstOrDefaultAsync(ct);

        _db.PlatformBankAccounts.Add(account);
        _db.AuditLogs.Add(AuditLog.Create(
            action: "PLATFORM_BANK_ACCOUNT_CHANGED",
            entityName: nameof(PlatformBankAccount),
            entityId: account.Id.ToString(),
            userId: user.Id,
            details: $"Đổi tài khoản nhận tiền nền tảng: {Describe(previous?.BankName, previous?.AccountNo)} → {Describe(account.BankName, account.AccountNo)}"));

        // Lưu cùng lúc bước TOTP đã dùng để mã không dùng lại được
        await _db.SaveChangesAsync(ct);

        return Result<PlatformBankAccountDto>.Success(new PlatformBankAccountDto(
            account.BankBin, account.BankName, account.AccountNo, account.AccountName,
            IsConfigured: true, ChangedAt: account.CreatedAt, ChangedByName: user.FullName));
    }

    /// <summary>Audit chỉ ghi 4 số cuối tài khoản.</summary>
    private static string Describe(string? bankName, string? accountNo) =>
        accountNo is null ? "SePay:DefaultBank" : $"{bankName} ****{accountNo[^Math.Min(4, accountNo.Length)..]}";
}
