using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Auth.Common;
using PanelForge.Application.Common.Interfaces.Authentication;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Auth.Commands.ResetPassword;

/// <summary>POST /api/auth/reset-password — đặt lại mật khẩu bằng OTP đã gửi qua email.</summary>
public sealed record ResetPasswordCommand(string Email, string Token, string NewPassword) : IRequest<Result<bool>>;

public sealed class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;

    public ResetPasswordCommandHandler(IPanelForgeDbContext dbContext, IPasswordHasher passwordHasher)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
    }

    public async Task<Result<bool>> Handle(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        var email = AuthSecrets.NormalizeEmail(command.Email);
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (user is null || !user.IsActive)
            return Result<bool>.Failure("Email hoặc mã xác thực không hợp lệ.");

        if (!AuthSecrets.OtpMatches(user.PasswordResetToken, command.Token))
        {
            user.RegisterFailedOtp();
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Result<bool>.Failure("Mã xác thực đặt lại mật khẩu không chính xác. Nhập sai nhiều lần sẽ phải yêu cầu mã mới.");
        }

        if (AuthSecrets.IsExpired(user.PasswordResetTokenExpiresAt))
            return Result<bool>.Failure("Mã xác thực đặt lại mật khẩu đã hết hạn. Vui lòng yêu cầu mã mới.");

        user.ResetPassword(_passwordHasher.HashPassword(command.NewPassword));
        await RefreshTokenRevocation.RevokeAllAsync(_dbContext, user.Id, cancellationToken, reason: "đặt lại mật khẩu");

        // Mật khẩu bị đặt lại (có thể vì nghi lộ) → thiết bị tin cậy không còn được bỏ qua 2FA.
        var trustedDevices = await _dbContext.UserRememberedDevices
            .Where(d => d.UserId == user.Id)
            .ToListAsync(cancellationToken);
        foreach (var device in trustedDevices)
            _dbContext.UserRememberedDevices.Remove(device);

        // OTP được gửi tới hộp thư nên đặt lại thành công đã chứng minh quyền sở hữu email.
        // Cần thiết cho tài khoản tạo từ lời mời Workspace.
        if (!user.IsEmailConfirmed)
            user.ConfirmEmail();

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}
