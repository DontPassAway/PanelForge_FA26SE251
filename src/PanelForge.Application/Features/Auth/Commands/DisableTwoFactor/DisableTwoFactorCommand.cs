using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Authentication;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Auth.Commands.DisableTwoFactor;

/// <summary>
/// POST /api/auth/disable-2fa — tắt 2FA. Phải chứng minh cả hai yếu tố: mã Authenticator hiện tại và
/// mật khẩu (nếu tài khoản có mật khẩu). Tắt xong thì xoá khoá bí mật và mọi thiết bị tin cậy.
/// </summary>
public sealed record DisableTwoFactorCommand(Guid UserId, string Code, string? CurrentPassword) : IRequest<Result<bool>>;

public sealed class DisableTwoFactorCommandHandler : IRequestHandler<DisableTwoFactorCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITotpService _totpService;

    public DisableTwoFactorCommandHandler(
        IPanelForgeDbContext dbContext, IPasswordHasher passwordHasher, ITotpService totpService)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _totpService = totpService;
    }

    public async Task<Result<bool>> Handle(DisableTwoFactorCommand command, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);
        if (user is null || !user.IsActive)
            return Unauthorized("Người dùng không tồn tại hoặc đã bị vô hiệu hóa.");

        if (!user.TwoFactorEnabled || string.IsNullOrEmpty(user.TwoFactorSecret))
            return Result<bool>.Failure("Tài khoản chưa bật 2FA.");

        if (!string.IsNullOrEmpty(user.PasswordHash)
            && (string.IsNullOrEmpty(command.CurrentPassword)
                || !_passwordHasher.VerifyPassword(command.CurrentPassword, user.PasswordHash)))
            return Unauthorized("Mật khẩu hiện tại không chính xác.");

        if (!_totpService.VerifyCode(user.TwoFactorSecret, command.Code))
            return Unauthorized("Mã xác thực 2FA không chính xác hoặc đã hết hạn.");

        user.DisableTwoFactor();

        var devices = await _dbContext.UserRememberedDevices
            .Where(d => d.UserId == user.Id)
            .ToListAsync(cancellationToken);
        foreach (var device in devices)
            _dbContext.UserRememberedDevices.Remove(device);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }

    private static Result<bool> Unauthorized(string message)
        => Result<bool>.Failure(message, ResultErrorCodes.Unauthorized);
}
