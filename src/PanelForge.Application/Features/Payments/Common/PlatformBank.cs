using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Payments.Models;

namespace PanelForge.Application.Features.Payments.Common;

/// <summary>
/// Tài khoản nhận tiền VietQR hiện hành của nền tảng (Q13): bản ghi mới nhất của <c>platform_bank_accounts</c>,
/// chưa có thì lấy <c>SePay:DefaultBank</c>. Mọi đơn nạp Ví / mở khóa đều chuyển vào tài khoản này, không theo Studio.
/// </summary>
public static class PlatformBank
{
    public sealed record Account(string Bin, string Name, string AccountNo, string AccountName);

    public static async Task<Account> CurrentAsync(IPanelForgeDbContext db, DefaultBankSettings fallback, CancellationToken ct)
    {
        var row = await db.PlatformBankAccounts
            .AsNoTracking()
            .Where(a => !a.IsDeleted)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new Account(a.BankBin, a.BankName, a.AccountNo, a.AccountName))
            .FirstOrDefaultAsync(ct);

        return row ?? new Account(fallback.Bin, fallback.Name, fallback.AccountNo, fallback.AccountName);
    }
}
