using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Identity;

namespace PanelForge.Domain.Entities.Payments;

/// <summary>
/// Ví độc giả lưu số dư tiền VNĐ và trạng thái gói VIP.
/// </summary>
public class ReaderWallet : BaseEntity
{
    public Guid UserId { get; private set; }
    public decimal BalanceVnd { get; private set; } = 0m;
    public bool IsVip { get; private set; } = false;
    public DateTime? VipExpiresAt { get; private set; }

    public User User { get; private set; } = default!;

    private ReaderWallet() { }

    public static ReaderWallet Create(Guid userId, decimal balanceVnd = 0m, bool isVip = false, DateTime? vipExpiresAt = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(balanceVnd);

        return new ReaderWallet
        {
            UserId = userId,
            BalanceVnd = balanceVnd,
            IsVip = isVip,
            VipExpiresAt = vipExpiresAt
        };
    }

    public void AddBalance(decimal amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        BalanceVnd += amount;
    }

    public void DeductBalance(decimal amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        if (BalanceVnd < amount)
            throw new InvalidOperationException($"Số dư trong ví không đủ ({BalanceVnd:N0}đ < {amount:N0}đ).");
        BalanceVnd -= amount;
    }

    public void SetVip(bool isVip, DateTime? expiresAt)
    {
        IsVip = isVip;
        VipExpiresAt = expiresAt;
    }
}

