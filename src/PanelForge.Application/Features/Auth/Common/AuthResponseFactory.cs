using PanelForge.Application.Features.Auth.Models;
using PanelForge.Application.Common.Interfaces.Authentication;
using PanelForge.Domain.Entities.Identity;

namespace PanelForge.Application.Features.Auth.Common;

/// <summary>Dựng AuthResponse dùng chung cho Register / Login / VerifyEmail / ExternalLogin / 2FA.</summary>
internal static class AuthResponseFactory
{
    public static UserDto ToUserDto(User user) => new(
        user.Id,
        user.Email,
        user.FullName,
        user.PhoneNumber,
        user.AvatarUrl,
        user.IsActive,
        user.IsEmailConfirmed,
        user.TwoFactorEnabled,
        user.Role.ToString(),
        user.CanCreateStudio);

    /// <summary>Đăng nhập thành công: phát JWT.</summary>
    public static AuthResponse SignedIn(
        User user,
        IJwtTokenGenerator jwtTokenGenerator,
        string message,
        string? rememberDeviceToken = null)
    {
        var (token, expiresAt) = jwtTokenGenerator.GenerateToken(user);
        return new AuthResponse(
            Token: token,
            ExpiresAt: expiresAt,
            User: ToUserDto(user),
            Message: message,
            RememberDeviceToken: rememberDeviceToken);
    }

    /// <summary>Tài khoản bật 2FA: yêu cầu nhập mã Authenticator, chưa phát JWT.</summary>
    public static AuthResponse TwoFactorChallenge(User user) => new(
        Token: null,
        ExpiresAt: null,
        User: null,
        RequiresTwoFactor: true,
        TwoFactorEmail: user.Email,
        Message: "Yêu cầu xác thực 2FA. Vui lòng nhập mã OTP 6 chữ số từ ứng dụng Authenticator.");

    /// <summary>Thành công nhưng không phát JWT (vd: vừa đăng ký, vừa bật 2FA).</summary>
    public static AuthResponse WithoutToken(User user, string message) => new(
        Token: null,
        ExpiresAt: null,
        User: ToUserDto(user),
        Message: message);
}
