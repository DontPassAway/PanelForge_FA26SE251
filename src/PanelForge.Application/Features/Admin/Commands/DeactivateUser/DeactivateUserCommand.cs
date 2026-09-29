using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Admin.Models;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Admin.Commands.DeactivateUser;

/// <summary>DELETE /api/admin/users/{id} — vô hiệu hóa tài khoản (không xóa dữ liệu).</summary>
public sealed record DeactivateUserCommand(Guid UserId) : IRequest<Result<AdminUserStatusDto>>;

public sealed class DeactivateUserCommandHandler
    : IRequestHandler<DeactivateUserCommand, Result<AdminUserStatusDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public DeactivateUserCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<AdminUserStatusDto>> Handle(
        DeactivateUserCommand command,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);
        if (user is null)
            return Result<AdminUserStatusDto>.Failure("Không tìm thấy người dùng.", ResultErrorCodes.NotFound);

        user.Deactivate();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<AdminUserStatusDto>.Success(new AdminUserStatusDto(user.Id, user.Email, user.IsActive));
    }
}
