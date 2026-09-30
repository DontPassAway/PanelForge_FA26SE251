using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Auth.Common;
using PanelForge.Application.Common.Interfaces.Authentication;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Auth.Commands.ChangePassword;

/// <summary>
/// POST /api/auth/change-password. Yêu cầu mật khẩu hiện tại + OTP email,
/// và thêm mã Authenticator nếu tài khoản bật 2FA (step-up authentication).
/// </summary>
public sealed record ChangePasswordCommand(
    Guid UserId,
    string CurrentPassword,
    string NewPassword,
    string? EmailOtp,
    string? TwoFactorCode) : IRequest<Result<bool>>;

public sealed class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITotpService _totpService;

    public ChangePasswordCommandHandler(IPanelForgeDbContext dbContext, IPasswordHasher passwordHasher, ITotpService totpService)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _totpService = totpService;
    }

    public async Task<Result<bool>> Handle(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);
        if (user is null || !user.IsActive)
            return Unauthorized("Người dùng không tồn tại hoặc đã bị vô hiệu hóa.");

        if (string.IsNullOrEmpty(user.PasswordHash))
            return Result<bool>.Failure("Tài khoản chưa được thiết lập mật khẩu (được tạo qua đăng nhập mạng xã hội).");

        if (!_passwordHasher.VerifyPassword(command.CurrentPassword, user.PasswordHash))
            return Unauthorized("Mật khẩu hiện tại không chính xác.");

        if (_passwordHasher.VerifyPassword(command.NewPassword, user.PasswordHash))
            return Result<bool>.Failure("Mật khẩu mới không được trùng với mật khẩu hiện tại.");

        if (!AuthSecrets.OtpMatches(user.PasswordResetToken, command.EmailOtp))
        {
            user.RegisterFailedOtp();
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Unauthorized("Mã OTP xác thực qua email không chính xác. Nhập sai nhiều lần sẽ phải yêu cầu mã mới.");
        }

        if (AuthSecrets.IsExpired(user.PasswordResetTokenExpiresAt))
            return Unauthorized("Mã OTP xác thực qua email đã hết hạn. Vui lòng yêu cầu mã mới.");

        if (user.TwoFactorEnabled)
        {
            if (string.IsNullOrWhiteSpace(command.TwoFactorCode))
                return Unauthorized("Tài khoản đã kích hoạt 2FA. Vui lòng cung cấp mã OTP 6 chữ số từ ứng dụng Google Authenticator.");

            if (string.IsNullOrEmpty(user.TwoFactorSecret))
                return Result<bool>.Failure("Chưa cấu hình khóa bí mật 2FA cho tài khoản này.");

            if (!_totpService.VerifyCode(user.TwoFactorSecret, command.TwoFactorCode))
                return Unauthorized("Mã xác thực 2FA không chính xác hoặc đã hết hạn.");
        }

        user.ResetPassword(_passwordHasher.HashPassword(command.NewPassword));
        await RefreshTokenRevocation.RevokeAllAsync(_dbContext, user.Id, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<bool>.Success(true);
    }

    private static Result<bool> Unauthorized(string message)
        => Result<bool>.Failure(message, ResultErrorCodes.Unauthorized);
}
