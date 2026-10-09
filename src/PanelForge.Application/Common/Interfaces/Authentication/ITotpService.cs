namespace PanelForge.Application.Common.Interfaces.Authentication;

/// <summary>Mã TOTP cho 2FA (Google / Microsoft Authenticator).</summary>
public interface ITotpService
{
    /// <summary>Sinh secret ngẫu nhiên, dạng Base32.</summary>
    string GenerateSecret();

    /// <summary>URI otpauth:// để app Authenticator quét QR.</summary>
    string BuildAuthenticatorUri(string accountEmail, string secret);

    /// <summary>Kiểm tra mã 6 số, cho phép lệch ±1 bước thời gian.</summary>
    bool VerifyCode(string secret, string code);
}
