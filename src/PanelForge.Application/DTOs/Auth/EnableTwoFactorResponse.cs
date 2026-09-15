namespace PanelForge.Application.DTOs.Auth;

public record EnableTwoFactorResponse(
    string SharedKey,
    string AuthenticatorUri,
    string FormattedKey
);
