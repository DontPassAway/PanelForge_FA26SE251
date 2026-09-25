using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.Users.Commands.GrantStudioPermission;

/// <summary>
/// BR-22: Administrator grants CanCreateStudio permission to a User.
/// </summary>
public sealed record GrantStudioCreationPermissionCommand(Guid UserId)
    : IRequest<Result<GrantStudioPermissionDto>>;

public sealed record GrantStudioPermissionDto(
    Guid UserId,
    string Email,
    bool CanCreateStudio);

public sealed class GrantStudioCreationPermissionCommandHandler
    : IRequestHandler<GrantStudioCreationPermissionCommand, Result<GrantStudioPermissionDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GrantStudioCreationPermissionCommandHandler(IPanelForgeDbContext dbContext)
        => _dbContext = dbContext;

    public async Task<Result<GrantStudioPermissionDto>> Handle(
        GrantStudioCreationPermissionCommand command,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);

        if (user is null)
            return Result<GrantStudioPermissionDto>.Failure($"Người dùng {command.UserId} không tồn tại.");

        if (!user.IsActive)
            return Result<GrantStudioPermissionDto>.Failure("Không thể cấp quyền cho tài khoản đã bị vô hiệu hóa.");

        user.GrantStudioCreation();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<GrantStudioPermissionDto>.Success(new GrantStudioPermissionDto(
            UserId: user.Id,
            Email: user.Email,
            CanCreateStudio: user.CanCreateStudio));
    }
}
