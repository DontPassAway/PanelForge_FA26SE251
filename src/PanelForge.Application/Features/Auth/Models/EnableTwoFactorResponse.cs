namespace PanelForge.Application.Features.Auth.Models;

public record EnableTwoFactorResponse(
    string SharedKey,
    string AuthenticatorUri,
    string FormattedKey
);
