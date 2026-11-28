using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Payments;

/// <summary>
/// Tài khoản ngân hàng của nền tảng nhận mọi tiền nạp Ví và mở khóa qua VietQR (Q13, BR-32).
/// Append-only: mỗi lần Publisher đổi tài khoản thêm một bản ghi mới, bản ghi mới nhất là tài khoản đang dùng.
/// Chưa có bản ghi nào thì dùng cấu hình <c>SePay:DefaultBank</c>.
/// </summary>
public class PlatformBankAccount : BaseEntity
{
    public string BankBin { get; private set; } = default!;
    public string BankName { get; private set; } = default!;
    public string AccountNo { get; private set; } = default!;
    public string AccountName { get; private set; } = default!;
    public Guid ChangedByUserId { get; private set; }

    private PlatformBankAccount() { }

    public static PlatformBankAccount Create(string bankBin, string bankName, string accountNo, string accountName, Guid changedByUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bankBin);
        ArgumentException.ThrowIfNullOrWhiteSpace(bankName);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);

        var bin = bankBin.Trim();
        var no = accountNo.Trim();
        if (bin.Length != 6 || !bin.All(char.IsAsciiDigit))
            throw new ArgumentException("Mã BIN ngân hàng phải gồm 6 chữ số.", nameof(bankBin));
        if (no.Length is < 6 or > 19 || !no.All(char.IsAsciiDigit))
            throw new ArgumentException("Số tài khoản phải gồm 6 đến 19 chữ số.", nameof(accountNo));

        return new PlatformBankAccount
        {
            BankBin = bin,
            BankName = bankName.Trim(),
            AccountNo = no,
            AccountName = accountName.Trim().ToUpperInvariant(),
            ChangedByUserId = changedByUserId
        };
    }
}
