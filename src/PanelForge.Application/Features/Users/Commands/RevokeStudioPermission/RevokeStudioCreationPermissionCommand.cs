using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.Users.Commands.RevokeStudioPermission;

/// <summary>
/// BR-22: Administrator revokes CanCreateStudio permission from a User.
/// </summary>
public sealed record RevokeStudioCreationPermissionCommand(Guid UserId)
    : IRequest<Result<RevokeStudioPermissionDto>>;

public sealed record RevokeStudioPermissionDto(
    Guid UserId,
    string Email,
    bool CanCreateStudio);

public sealed class RevokeStudioCreationPermissionCommandHandler
    : IRequestHandler<RevokeStudioCreationPermissionCommand, Result<RevokeStudioPermissionDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public RevokeStudioCreationPermissionCommandHandler(IPanelForgeDbContext dbContext)
        => _dbContext = dbContext;

    public async Task<Result<RevokeStudioPermissionDto>> Handle(
        RevokeStudioCreationPermissionCommand command,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);

        if (user is null)
            return Result<RevokeStudioPermissionDto>.Failure($"Người dùng {command.UserId} không tồn tại.");

        user.RevokeStudioCreation();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<RevokeStudioPermissionDto>.Success(new RevokeStudioPermissionDto(
            UserId: user.Id,
            Email: user.Email,
            CanCreateStudio: user.CanCreateStudio));
    }
}
