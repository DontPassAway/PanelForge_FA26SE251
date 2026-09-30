using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Auth.Common;

namespace PanelForge.Application.Features.Auth.Commands.Logout;

/// <summary>POST /api/auth/logout — thu hồi refresh token của phiên hiện tại (không có token thì bỏ qua).</summary>
public sealed record LogoutCommand(Guid UserId, string? RefreshToken) : IRequest<Result<bool>>;

public sealed class LogoutCommandHandler : IRequestHandler<LogoutCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public LogoutCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<bool>> Handle(LogoutCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
            return Result<bool>.Success(true);

        var hash = AuthSecrets.HashToken(command.RefreshToken.Trim());
        var stored = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.UserId == command.UserId, cancellationToken);
        if (stored is not null)
        {
            stored.Revoke();
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        return Result<bool>.Success(true);
    }
}
