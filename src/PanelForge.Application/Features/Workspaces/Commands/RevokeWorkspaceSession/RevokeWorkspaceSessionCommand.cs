using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Audit;

namespace PanelForge.Application.Features.Workspaces.Commands.RevokeWorkspaceSession;

public sealed record RevokeWorkspaceSessionCommand(Guid WorkspaceId, Guid SessionId, Guid RequestedByUserId)
    : IRequest<Result<Unit>>;

public sealed class RevokeWorkspaceSessionCommandHandler
    : IRequestHandler<RevokeWorkspaceSessionCommand, Result<Unit>>
{
    private readonly IPanelForgeDbContext _db;

    public RevokeWorkspaceSessionCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<Unit>> Handle(RevokeWorkspaceSessionCommand command, CancellationToken ct)
    {
        var activeTokens = await _db.RefreshTokens
            .Where(t => t.SessionId == command.SessionId && t.RevokedAt == null)
            .Include(t => t.User)
            .ToListAsync(ct);

        if (activeTokens.Count == 0)
            return Result<Unit>.Failure("Phiên đăng nhập không tồn tại hoặc đã kết thúc.", ResultErrorCodes.NotFound);

        var targetUserId = activeTokens[0].UserId;
        var isMember = await _db.WorkspaceMembers.AnyAsync(
            m => m.WorkspaceId == command.WorkspaceId && m.UserId == targetUserId, ct);

        if (!isMember)
            return Result<Unit>.Failure("Phiên đăng nhập này không thuộc thành viên của Workspace.", ResultErrorCodes.Forbidden);

        foreach (var token in activeTokens)
        {
            token.Revoke();
        }

        var actor = await _db.Users.FindAsync(new object[] { command.RequestedByUserId }, ct);
        var targetUser = activeTokens[0].User;
        var deviceName = activeTokens[0].DeviceName ?? "Thiết bị không xác định";

        _db.AuditLogs.Add(AuditLog.Create(
            action: "REVOKE_SESSION",
            entityName: "Session",
            entityId: command.SessionId.ToString(),
            workspaceId: command.WorkspaceId,
            userId: command.RequestedByUserId,
            userEmail: actor?.Email,
            details: $"Producer đã thu hồi phiên đăng nhập {command.SessionId} của {targetUser?.Email} ({deviceName})."
        ));

        await _db.SaveChangesAsync(ct);
        return Result<Unit>.Success(Unit.Value);
    }
}
