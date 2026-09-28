using OtpNet;
using PanelForge.Application.Common.Interfaces.Authentication;

namespace PanelForge.Infrastructure.Authentication;

public sealed class OtpNetTotpService : ITotpService
{
    private const string Issuer = "PanelForge";

    public string GenerateSecret()
        => Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));

    public string BuildAuthenticatorUri(string accountEmail, string secret)
        => $"otpauth://totp/{Issuer}:{Uri.EscapeDataString(accountEmail)}?secret={secret}&issuer={Issuer}&digits=6&period=30";

    public bool VerifyCode(string secret, string code)
    {
        var totp = new Totp(Base32Encoding.ToBytes(secret));
        return totp.VerifyTotp(code.Trim(), out _, new VerificationWindow(previous: 1, future: 1));
    }
}
