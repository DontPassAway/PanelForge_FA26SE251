namespace PanelForge.Application.DTOs.Auth;

public record AuthResponse(
    string? Token = null,
    DateTime? ExpiresAt = null,
    UserDto? User = null,
    bool RequiresTwoFactor = false,
    string? TwoFactorEmail = null,
    string? Message = null
);
