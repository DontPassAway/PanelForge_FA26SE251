using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Auth.Models;
using PanelForge.Application.Features.Auth.Common;
using PanelForge.Application.Common.Interfaces.Authentication;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Auth.Commands.EnableTwoFactor;

/// <summary>
/// POST /api/auth/enable-2fa — sinh secret và URI cho app Authenticator.
/// 2FA chỉ thực sự bật sau khi người dùng xác nhận mã qua verify-2fa.
/// </summary>
public sealed record EnableTwoFactorCommand(Guid UserId) : IRequest<Result<EnableTwoFactorResponse>>;

public sealed class EnableTwoFactorCommandHandler : IRequestHandler<EnableTwoFactorCommand, Result<EnableTwoFactorResponse>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly ITotpService _totpService;

    public EnableTwoFactorCommandHandler(IPanelForgeDbContext dbContext, ITotpService totpService)
    {
        _dbContext = dbContext;
        _totpService = totpService;
    }

    public async Task<Result<EnableTwoFactorResponse>> Handle(EnableTwoFactorCommand command, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);
        if (user is null || !user.IsActive)
            return Result<EnableTwoFactorResponse>.Failure(
                "Người dùng không tồn tại hoặc đã bị vô hiệu hóa.", ResultErrorCodes.Unauthorized);

        // Không ghi đè khoá đang dùng: nếu bỏ dở thiết lập, Authenticator cũ sẽ không còn khớp và người dùng bị khoá ngoài.
        if (user.TwoFactorEnabled)
            return Result<EnableTwoFactorResponse>.Failure("2FA đang bật. Hãy tắt 2FA trước khi thiết lập lại.");

        var secret = _totpService.GenerateSecret();
        user.SetTwoFactorSecret(secret);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<EnableTwoFactorResponse>.Success(new EnableTwoFactorResponse(
            SharedKey: secret,
            AuthenticatorUri: _totpService.BuildAuthenticatorUri(user.Email, secret),
            FormattedKey: AuthSecrets.FormatSharedKey(secret)));
    }
}
