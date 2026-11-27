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

    public long? MatchTimeStep(string secret, string code)
    {
        var trimmed = code?.Trim() ?? string.Empty;
        if (trimmed.Length != 6 || !trimmed.All(char.IsAsciiDigit))
            return null;

        var totp = new Totp(Base32Encoding.ToBytes(secret));
        return totp.VerifyTotp(trimmed, out var timeStep, new VerificationWindow(previous: 1, future: 1))
            ? timeStep
            : null;
    }
}
