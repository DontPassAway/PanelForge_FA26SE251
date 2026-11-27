namespace PanelForge.Application.Common.Interfaces.Authentication;

/// <summary>Mã TOTP cho 2FA (Google / Microsoft Authenticator).</summary>
public interface ITotpService
{
    /// <summary>Sinh secret ngẫu nhiên, dạng Base32.</summary>
    string GenerateSecret();

    /// <summary>URI otpauth:// để app Authenticator quét QR.</summary>
    string BuildAuthenticatorUri(string accountEmail, string secret);

    /// <summary>
    /// Kiểm tra mã 6 số (cho phép lệch ±1 bước thời gian 30 giây). Trả về bước thời gian khớp, null nếu sai.
    /// Bước trả về dùng cho <c>User.TryConsumeTwoFactorStep</c> để chặn dùng lại mã (RFC 6238 §5.2).
    /// </summary>
    long? MatchTimeStep(string secret, string code);
}
