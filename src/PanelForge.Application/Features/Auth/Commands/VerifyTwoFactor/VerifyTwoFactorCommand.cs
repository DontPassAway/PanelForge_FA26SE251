using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Auth.Models;
using PanelForge.Application.Features.Auth.Common;
using PanelForge.Application.Common.Interfaces.Authentication;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Identity;

namespace PanelForge.Application.Features.Auth.Commands.VerifyTwoFactor;

/// <summary>
/// POST /api/auth/verify-2fa, hai trường hợp:
///   - CurrentUserId có giá trị: người dùng đã đăng nhập xác nhận mã để BẬT 2FA.
///   - CurrentUserId null: bước thử thách 2FA khi đăng nhập (theo Email), thành công thì phát JWT
///     và (tuỳ chọn) ghi nhớ thiết bị 30 ngày.
/// </summary>
public sealed record VerifyTwoFactorCommand(
    Guid? CurrentUserId,
    string Code,
    string? Email,
    bool RememberDevice,
    string? DeviceName,
    string? IpAddress = null,
    string? UserAgent = null,
    string? ChallengeEmail = null) : IRequest<Result<AuthResponse>>;

public sealed class VerifyTwoFactorCommandHandler : IRequestHandler<VerifyTwoFactorCommand, Result<AuthResponse>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly ITotpService _totpService;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public VerifyTwoFactorCommandHandler(
        IPanelForgeDbContext dbContext,
        ITotpService totpService,
        IJwtTokenGenerator jwtTokenGenerator)
    {
        _dbContext = dbContext;
        _totpService = totpService;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public Task<Result<AuthResponse>> Handle(VerifyTwoFactorCommand command, CancellationToken cancellationToken)
    {
        // Thử thách đăng nhập đang mở (đã qua bước mật khẩu) được ưu tiên, kể cả khi trình duyệt còn cookie của phiên khác.
        if (!string.IsNullOrWhiteSpace(command.ChallengeEmail))
            return CompleteLoginChallengeAsync(command, cancellationToken);

        return command.CurrentUserId.HasValue
            ? ConfirmSetupAsync(command.CurrentUserId.Value, command.Code, cancellationToken)
            : Task.FromResult(Unauthorized("Phiên xác thực 2FA đã hết hạn hoặc chưa qua bước đăng nhập. Vui lòng đăng nhập lại."));
    }

    private async Task<Result<AuthResponse>> ConfirmSetupAsync(Guid userId, string code, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || !user.IsActive)
            return Unauthorized("Người dùng không tồn tại hoặc đã bị vô hiệu hóa.");

        if (string.IsNullOrEmpty(user.TwoFactorSecret))
            return Result<AuthResponse>.Failure("Chưa khởi tạo khóa bảo mật 2FA. Vui lòng gọi enable-2fa trước.");

        if (!_totpService.VerifyCode(user.TwoFactorSecret, code))
            return Unauthorized("Mã xác thực 2FA không chính xác.");

        user.EnableTwoFactor();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<AuthResponse>.Success(AuthResponseFactory.WithoutToken(
            user, "Bảo mật 2FA đã được kích hoạt thành công cho tài khoản của bạn."));
    }

    private async Task<Result<AuthResponse>> CompleteLoginChallengeAsync(
        VerifyTwoFactorCommand command, CancellationToken cancellationToken)
    {
        // Email lấy từ thử thách do server cấp ở bước mật khẩu; email trong body (nếu có) phải trùng.
        var email = AuthSecrets.NormalizeEmail(command.ChallengeEmail!);
        if (!string.IsNullOrWhiteSpace(command.Email) && AuthSecrets.NormalizeEmail(command.Email) != email)
            return Unauthorized("Thông tin tài khoản hoặc cấu hình 2FA không hợp lệ.");

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null || !user.IsActive || !user.TwoFactorEnabled || string.IsNullOrEmpty(user.TwoFactorSecret))
            return Unauthorized("Thông tin tài khoản hoặc cấu hình 2FA không hợp lệ.");

        if (!_totpService.VerifyCode(user.TwoFactorSecret, command.Code))
            return Unauthorized("Mã xác thực 2FA không chính xác.");

        string? rememberToken = null;
        if (command.RememberDevice)
        {
            rememberToken = AuthSecrets.NewRememberDeviceToken();
            _dbContext.UserRememberedDevices.Add(UserRememberedDevice.Create(
                userId: user.Id,
                tokenHash: AuthSecrets.HashToken(rememberToken),
                expiresAt: DateTime.UtcNow.Add(AuthSecrets.RememberDeviceLifetime),
                deviceName: command.DeviceName));
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var message = command.RememberDevice
            ? "Xác thực 2FA thành công. Thiết bị này đã được ghi nhớ trong 30 ngày."
            : "Xác thực 2FA và đăng nhập thành công.";

        return Result<AuthResponse>.Success(
            await AuthResponseFactory.SignedInAsync(_dbContext, cancellationToken, user, _jwtTokenGenerator, message, rememberToken,
                deviceName: DeviceDetector.DetectDevice(command.UserAgent), ipAddress: command.IpAddress, userAgent: command.UserAgent));
    }

    private static Result<AuthResponse> Unauthorized(string message)
        => Result<AuthResponse>.Failure(message, ResultErrorCodes.Unauthorized);
}
