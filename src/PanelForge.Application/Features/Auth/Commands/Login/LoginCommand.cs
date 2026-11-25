using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Auth.Models;
using PanelForge.Application.Features.Auth.Common;
using PanelForge.Application.Common.Interfaces.Authentication;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Auth.Commands.Login;

/// <summary>
/// POST /api/auth/login. Nếu tài khoản bật 2FA: thiết bị đã ghi nhớ (còn hạn) được cấp JWT luôn,
/// ngược lại trả về thử thách 2FA.
/// </summary>
public sealed record LoginCommand(
    string Email,
    string Password,
    string? RememberDeviceToken = null,
    string? DeviceName = null,
    string? IpAddress = null,
    string? UserAgent = null)
    : IRequest<Result<AuthResponse>>;

public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, Result<AuthResponse>>
{
    private const string InvalidCredentials = "Email hoặc mật khẩu không chính xác.";

    private readonly IPanelForgeDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public LoginCommandHandler(IPanelForgeDbContext dbContext, IPasswordHasher passwordHasher, IJwtTokenGenerator jwtTokenGenerator)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<Result<AuthResponse>> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        var email = AuthSecrets.NormalizeEmail(command.Email);
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null || !user.IsActive || string.IsNullOrEmpty(user.PasswordHash)
            || !_passwordHasher.VerifyPassword(command.Password, user.PasswordHash))
            return Result<AuthResponse>.Failure(InvalidCredentials, ResultErrorCodes.Unauthorized);

        if (!user.IsEmailConfirmed)
            return Result<AuthResponse>.Failure(
                "Tài khoản chưa được kích hoạt. Vui lòng kiểm tra email và nhập mã PIN để xác thực.",
                ResultErrorCodes.Unauthorized);

        if (!user.TwoFactorEnabled)
            return Result<AuthResponse>.Success(
                await AuthResponseFactory.SignedInAsync(
                    _dbContext,
                    cancellationToken,
                    user,
                    _jwtTokenGenerator,
                    "Đăng nhập thành công.",
                    rememberDeviceToken: null,
                    deviceName: command.DeviceName,
                    ipAddress: command.IpAddress,
                    userAgent: command.UserAgent));

        if (!string.IsNullOrWhiteSpace(command.RememberDeviceToken))
        {
            var tokenHash = AuthSecrets.HashToken(command.RememberDeviceToken.Trim());
            var device = await _dbContext.UserRememberedDevices
                .FirstOrDefaultAsync(d => d.UserId == user.Id && d.TokenHash == tokenHash, cancellationToken);

            // Thiết bị tin cậy còn hạn (30 ngày) → bỏ qua thử thách 2FA
            if (device is not null && !device.IsExpired)
                return Result<AuthResponse>.Success(await AuthResponseFactory.SignedInAsync(
                    _dbContext,
                    cancellationToken, 
                    user,
                    _jwtTokenGenerator,
                    "Đăng nhập thành công (Thiết bị tin cậy được ghi nhớ).",
                    command.RememberDeviceToken,
                    deviceName: device.DeviceName ?? command.DeviceName,
                    ipAddress: command.IpAddress,
                    userAgent: command.UserAgent));
        }

        return Result<AuthResponse>.Success(AuthResponseFactory.TwoFactorChallenge(user));
    }
}
