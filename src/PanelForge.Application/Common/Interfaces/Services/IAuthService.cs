using PanelForge.Application.DTOs.Auth;

namespace PanelForge.Application.Services;

public interface IAuthService
{
    // Authentication cơ bản
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, string? rememberDeviceToken = null, CancellationToken cancellationToken = default);

    // Quản lý mật khẩu
    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default);
    Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);

    // Xác thực tài khoản
    Task<AuthResponse> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken = default);
    Task ResendVerificationEmailAsync(ResendVerificationRequest request, CancellationToken cancellationToken = default);

    // Đăng nhập Social (Firebase)
    Task<AuthResponse> ExternalLoginAsync(ExternalLoginRequest request, CancellationToken cancellationToken = default);

    // Bảo mật 2FA (Two-Factor Authentication)
    Task<EnableTwoFactorResponse> EnableTwoFactorAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<AuthResponse> VerifyTwoFactorAsync(Guid? currentUserId, VerifyTwoFactorRequest request, CancellationToken cancellationToken = default);
    Task ForgetDeviceAsync(string? rememberDeviceToken, Guid? currentUserId = null, CancellationToken cancellationToken = default);
}
