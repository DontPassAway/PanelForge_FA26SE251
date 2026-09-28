using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Admin.Common;
using PanelForge.Application.Features.Admin.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Admin.Commands.AssignUserRole;

/// <summary>
/// POST /api/admin/assign-producer | assign-role.
/// SystemRole chỉ gồm Admin | User (E-11). "Producer" là WorkspaceRole, nên gán "Producer" ở đây
/// nghĩa là cấp quyền tạo Studio (CanCreateStudio = true, BR-22); SystemRole vẫn là User.
/// </summary>
public sealed record AssignUserRoleCommand(Guid UserId, string? NewRole)
    : IRequest<Result<AssignUserRoleResponse>>;

public sealed class AssignUserRoleCommandHandler
    : IRequestHandler<AssignUserRoleCommand, Result<AssignUserRoleResponse>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public AssignUserRoleCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<AssignUserRoleResponse>> Handle(
        AssignUserRoleCommand command,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);
        if (user is null)
            return Result<AssignUserRoleResponse>.Failure("Không tìm thấy người dùng.", ResultErrorCodes.NotFound);

        var rawRole = command.NewRole?.Trim() ?? string.Empty;

        if (string.Equals(rawRole, "Producer", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rawRole, "GrantProducer", StringComparison.OrdinalIgnoreCase))
        {
            if (user.Role == SystemRole.Admin)
                return Result<AssignUserRoleResponse>.Failure(
                    "Không thể cấp quyền Producer cho tài khoản Administrator (BR-07, BR-22).");

            user.GrantStudioCreation();
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Result<AssignUserRoleResponse>.Success(new AssignUserRoleResponse(
                $"Đã cấp quyền tạo Studio cho tài khoản {user.Email}. Người dùng sẽ là Producer của Studio họ tạo.",
                user.Id,
                user.Role.ToString(),
                user.CanCreateStudio));
        }

        if (Enum.TryParse<SystemRole>(rawRole, true, out var parsedSystemRole) && Enum.IsDefined(parsedSystemRole))
        {
            if (parsedSystemRole == SystemRole.Admin && user.Role != SystemRole.Admin)
            {
                var blockReason = await AdminPromotionGuard.GetBlockReasonAsync(_dbContext, user.Id, cancellationToken);
                if (blockReason is not null)
                    return Result<AssignUserRoleResponse>.Failure(blockReason);

                user.RevokeStudioCreation();
            }

            user.AssignSystemRole(parsedSystemRole);
            if (parsedSystemRole == SystemRole.User)
                user.RevokeStudioCreation();

            await _dbContext.SaveChangesAsync(cancellationToken);

            return Result<AssignUserRoleResponse>.Success(new AssignUserRoleResponse(
                $"Đã cập nhật vai trò của người dùng {user.Email} thành {parsedSystemRole}.",
                user.Id,
                parsedSystemRole.ToString(),
                user.CanCreateStudio));
        }

        return Result<AssignUserRoleResponse>.Failure(
            $"Vai trò '{command.NewRole}' không hợp lệ. Hợp lệ: Producer, Admin, User.");
    }
}
