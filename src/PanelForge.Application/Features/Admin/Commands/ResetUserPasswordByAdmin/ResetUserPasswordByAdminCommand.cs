using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Admin.Models;
using PanelForge.Application.Common.Interfaces.Authentication;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Admin.Commands.ResetUserPasswordByAdmin;

/// <summary>POST /api/admin/users/{id}/reset-password — Administrator đặt lại mật khẩu.</summary>
public sealed record ResetUserPasswordByAdminCommand(Guid UserId, string NewPassword)
    : IRequest<Result<AdminUserStatusDto>>;

public sealed class ResetUserPasswordByAdminCommandHandler
    : IRequestHandler<ResetUserPasswordByAdminCommand, Result<AdminUserStatusDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;

    public ResetUserPasswordByAdminCommandHandler(IPanelForgeDbContext dbContext, IPasswordHasher passwordHasher)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
    }

    public async Task<Result<AdminUserStatusDto>> Handle(
        ResetUserPasswordByAdminCommand command,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);
        if (user is null)
            return Result<AdminUserStatusDto>.Failure("Không tìm thấy người dùng.", ResultErrorCodes.NotFound);

        user.ResetPassword(_passwordHasher.HashPassword(command.NewPassword));
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<AdminUserStatusDto>.Success(new AdminUserStatusDto(user.Id, user.Email, user.IsActive));
    }
}
