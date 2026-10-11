using PanelForge.Application.Features.Auth.Models;
using PanelForge.Application.Common.Interfaces.Authentication;
using PanelForge.Application.Common.Interfaces.Persistence;
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

    /// <summary>Đăng nhập thành công: phát access JWT + refresh token (lưu hash, hạn 30 ngày).</summary>
    public static async Task<AuthResponse> SignedInAsync(
        IPanelForgeDbContext dbContext,
        CancellationToken cancellationToken,
        User user,
        IJwtTokenGenerator jwtTokenGenerator,
        string message,
        string? rememberDeviceToken = null,
        string? deviceName = null,
        string? ipAddress = null,
        string? userAgent = null)
    {
        var refreshToken = AuthSecrets.NewRefreshToken();
        var now = DateTime.UtcNow;
        var policy = await SessionPolicy.ForUserAsync(dbContext, user.Id, cancellationToken);
        var session = RefreshToken.Create(
            user.Id,
            AuthSecrets.HashToken(refreshToken),
            policy.ExpiryFor(now, now),
            deviceName: deviceName,
            ipAddress: ipAddress,
            userAgent: userAgent,
            lastActiveAt: now);
        await policy.EnforceMaxSessionsAsync(dbContext, user.Id, session.SessionId, now, cancellationToken);
        dbContext.RefreshTokens.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Build(user, jwtTokenGenerator, message, rememberDeviceToken, refreshToken);
    }

    /// <summary>Dựng AuthResponse từ refresh token đã được lưu sẵn (dùng khi xoay vòng token).</summary>
    public static AuthResponse Build(
        User user, IJwtTokenGenerator jwtTokenGenerator, string message, string? rememberDeviceToken, string refreshToken)
    {
        var (token, expiresAt) = jwtTokenGenerator.GenerateToken(user);
        return new AuthResponse(
            Token: token,
            ExpiresAt: expiresAt,
            User: ToUserDto(user),
            Message: message,
            RememberDeviceToken: rememberDeviceToken,
            RefreshToken: refreshToken);
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
