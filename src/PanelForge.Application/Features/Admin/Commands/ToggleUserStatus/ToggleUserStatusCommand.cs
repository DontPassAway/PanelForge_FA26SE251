using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Admin.Models;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Admin.Commands.ToggleUserStatus;

/// <summary>PATCH /api/admin/users/{id}/toggle-status — khóa / mở khóa tài khoản.</summary>
public sealed record ToggleUserStatusCommand(Guid UserId) : IRequest<Result<AdminUserStatusDto>>;

public sealed class ToggleUserStatusCommandHandler
    : IRequestHandler<ToggleUserStatusCommand, Result<AdminUserStatusDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public ToggleUserStatusCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<AdminUserStatusDto>> Handle(
        ToggleUserStatusCommand command,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);
        if (user is null)
            return Result<AdminUserStatusDto>.Failure("Không tìm thấy người dùng.", ResultErrorCodes.NotFound);

        if (user.IsActive)
            user.Deactivate();
        else
            user.Activate();

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<AdminUserStatusDto>.Success(new AdminUserStatusDto(user.Id, user.Email, user.IsActive));
    }
}
