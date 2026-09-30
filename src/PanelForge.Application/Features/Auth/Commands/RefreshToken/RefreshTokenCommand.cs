using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Authentication;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Auth.Common;
using PanelForge.Application.Features.Auth.Models;
using PanelForge.Domain.Entities.Identity;

namespace PanelForge.Application.Features.Auth.Commands.RefreshToken;

/// <summary>
/// POST /api/auth/refresh — đổi refresh token lấy access JWT mới. Xoay vòng: token cũ bị thu hồi, phát token mới.
/// Token đã thu hồi mà bị dùng lại (nghi bị đánh cắp) → thu hồi toàn bộ phiên của user.
/// </summary>
public sealed record RefreshTokenCommand(string RefreshToken) : IRequest<Result<AuthResponse>>;

public sealed class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, Result<AuthResponse>>
{
    private const string Invalid = "Phiên đăng nhập không hợp lệ hoặc đã hết hạn. Vui lòng đăng nhập lại.";

    private readonly IPanelForgeDbContext _dbContext;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public RefreshTokenCommandHandler(IPanelForgeDbContext dbContext, IJwtTokenGenerator jwtTokenGenerator)
    {
        _dbContext = dbContext;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<Result<AuthResponse>> Handle(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
            return Unauthorized();

        var hash = AuthSecrets.HashToken(command.RefreshToken.Trim());
        var stored = await _dbContext.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (stored is null)
            return Unauthorized();

        if (stored.IsRevoked)
        {
            await RefreshTokenRevocation.RevokeAllAsync(_dbContext, stored.UserId, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Unauthorized();
        }

        if (stored.IsExpired || !stored.User.IsActive)
            return Unauthorized();

        stored.Revoke();
        var newToken = AuthSecrets.NewRefreshToken();
        _dbContext.RefreshTokens.Add(Domain.Entities.Identity.RefreshToken.Create(
            stored.UserId, AuthSecrets.HashToken(newToken), DateTime.UtcNow.Add(AuthSecrets.RefreshTokenLifetime)));
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<AuthResponse>.Success(AuthResponseFactory.Build(
            stored.User, _jwtTokenGenerator, "Làm mới phiên đăng nhập thành công.", null, newToken));
    }

    private static Result<AuthResponse> Unauthorized() => Result<AuthResponse>.Failure(Invalid, ResultErrorCodes.Unauthorized);
}
