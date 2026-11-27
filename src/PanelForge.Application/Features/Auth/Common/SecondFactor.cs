using PanelForge.Application.Common.Interfaces.Authentication;
using PanelForge.Domain.Entities.Identity;

namespace PanelForge.Application.Features.Auth.Common;

/// <summary>
/// Kiểm tra yếu tố thứ hai của tài khoản bật 2FA: mã TOTP 6 số từ Authenticator, hoặc mã khôi phục
/// "ABCDE-FGHJK" khi mất điện thoại. Cả hai đều chỉ dùng được một lần. Gọi xong phải SaveChanges.
/// </summary>
internal static class SecondFactor
{
    public enum Outcome { Invalid, AlreadyUsed, Totp, RecoveryCode }

    public static Outcome Verify(User user, ITotpService totp, string? code)
    {
        if (string.IsNullOrEmpty(user.TwoFactorSecret) || string.IsNullOrWhiteSpace(code))
            return Outcome.Invalid;

        var trimmed = code.Trim();
        if (trimmed.Length == 6 && trimmed.All(char.IsAsciiDigit))
        {
            if (totp.MatchTimeStep(user.TwoFactorSecret, trimmed) is not { } step)
                return Outcome.Invalid;
            return user.TryConsumeTwoFactorStep(step) ? Outcome.Totp : Outcome.AlreadyUsed;
        }

        var recovery = AuthSecrets.NormalizeRecoveryCode(trimmed);
        return recovery is not null && user.TryConsumeRecoveryCode(AuthSecrets.HashRecoveryCode(recovery))
            ? Outcome.RecoveryCode
            : Outcome.Invalid;
    }

    /// <summary>Sinh bộ mã mới, thay toàn bộ mã cũ trên user; trả mã thô để hiển thị một lần.</summary>
    public static IReadOnlyList<string> IssueRecoveryCodes(User user)
    {
        var codes = AuthSecrets.NewRecoveryCodes();
        user.ReplaceRecoveryCodes(codes.Select(c => AuthSecrets.HashRecoveryCode(AuthSecrets.NormalizeRecoveryCode(c)!)));
        return codes;
    }
}
