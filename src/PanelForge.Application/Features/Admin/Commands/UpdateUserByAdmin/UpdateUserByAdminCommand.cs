using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Admin.Common;
using PanelForge.Application.Features.Admin.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Admin.Commands.UpdateUserByAdmin;

/// <summary>PUT /api/admin/users/{id} — cập nhật thông tin và vai trò tài khoản (UC-01).</summary>
public sealed record UpdateUserByAdminCommand(
    Guid UserId,
    string FullName,
    string? PhoneNumber,
    SystemRole Role,
    bool IsActive) : IRequest<Result<AdminUserDto>>;

public sealed class UpdateUserByAdminCommandHandler
    : IRequestHandler<UpdateUserByAdminCommand, Result<AdminUserDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public UpdateUserByAdminCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<AdminUserDto>> Handle(
        UpdateUserByAdminCommand command,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);
        if (user is null)
            return Result<AdminUserDto>.Failure("Không tìm thấy người dùng.", ResultErrorCodes.NotFound);

        if (!Enum.IsDefined(command.Role))
            return Result<AdminUserDto>.Failure("Vai trò hệ thống không hợp lệ. Hợp lệ: Admin, User.");

        if (command.Role == SystemRole.Admin && user.Role != SystemRole.Admin)
        {
            var blockReason = await AdminPromotionGuard.GetBlockReasonAsync(_dbContext, user.Id, cancellationToken);
            if (blockReason is not null)
                return Result<AdminUserDto>.Failure(blockReason);

            user.RevokeStudioCreation();
        }

        user.UpdateProfile(command.FullName, command.PhoneNumber, user.AvatarUrl);
        user.AssignSystemRole(command.Role);

        if (command.IsActive)
            user.Activate();
        else
            user.Deactivate();

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<AdminUserDto>.Success(AdminUserMapper.ToDto(user));
    }
}
