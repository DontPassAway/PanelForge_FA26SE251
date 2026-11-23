using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Audit;

namespace PanelForge.Application.Features.Auth.Commands.RevokeSession;

public sealed record RevokeSessionCommand(Guid UserId, Guid SessionId, string? IpAddress = null)
    : IRequest<Result<string>>;

public sealed class RevokeSessionCommandHandler : IRequestHandler<RevokeSessionCommand, Result<string>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public RevokeSessionCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<string>> Handle(RevokeSessionCommand request, CancellationToken cancellationToken)
    {
        var tokens = await _dbContext.RefreshTokens
            .Where(t => t.UserId == request.UserId && t.SessionId == request.SessionId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        if (tokens.Count == 0)
            return Result<string>.Failure("Không tìm thấy phiên đăng nhập hoặc thiết bị đã đăng xuất.", ResultErrorCodes.NotFound);

        foreach (var token in tokens)
            token.Revoke();

        _dbContext.AuditLogs.Add(AuditLog.Create(
            action: "REVOKE_SESSION",
            entityName: "Session",
            entityId: request.SessionId.ToString(),
            userId: request.UserId,
            ipAddress: request.IpAddress,
            details: $"Đăng xuất từ xa thiết bị ({tokens[0].DeviceName ?? "Không xác định"})."));

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<string>.Success("Đã đăng xuất thiết bị thành công.");
    }
}
