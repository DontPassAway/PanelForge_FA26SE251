namespace PanelForge.Application.Features.Auth.Models;

public record AuthResponse(
    string? Token = null,
    DateTime? ExpiresAt = null,
    UserDto? User = null,
    bool RequiresTwoFactor = false,
    string? TwoFactorEmail = null,
    string? Message = null,
    string? RememberDeviceToken = null,
    string? RefreshToken = null,
    // Mã khôi phục 2FA thô: chỉ có khi vừa bật 2FA (verify-2fa bước thiết lập), hiển thị cho người dùng đúng một lần
    IReadOnlyList<string>? RecoveryCodes = null
);
