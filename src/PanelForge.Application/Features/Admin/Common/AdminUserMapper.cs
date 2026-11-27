using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Features.Admin.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Admin.Common;

internal static class AdminUserMapper
{
    public static AdminUserDto ToDto(User user) => new(
        user.Id,
        user.Email,
        user.FullName,
        user.PhoneNumber,
        user.Role.ToString(),
        user.IsActive,
        user.IsEmailConfirmed,
        user.CreatedAt);
}

internal static class AdminPromotionGuard
{
    /// <summary>
    /// BR-07: Administrator không được có dòng workspace_members và không sở hữu Studio nào.
    /// Trả về lý do chặn nâng quyền Admin, hoặc null nếu hợp lệ.
    /// </summary>
    public static async Task<string?> GetBlockReasonAsync(
        IPanelForgeDbContext dbContext, Guid userId, CancellationToken cancellationToken)
    {
        var ownedCount = await dbContext.StudioWorkspaces
            .CountAsync(w => w.OwnerId == userId, cancellationToken);
        if (ownedCount > 0)
            return $"Không thể nâng quyền Administrator: người dùng đang sở hữu {ownedCount} Studio. Hãy chuyển quyền sở hữu hoặc xóa Studio trước (BR-07).";

        var memberCount = await dbContext.WorkspaceMembers
            .CountAsync(m => m.UserId == userId, cancellationToken);
        if (memberCount > 0)
            return $"Không thể nâng quyền Administrator: người dùng đang là thành viên của {memberCount} Workspace. Hãy gỡ khỏi các Workspace trước (BR-07).";

        return null;
    }
}

internal static class SinglePublisherGuard
{
    public const string ConflictMessage =
        "Hệ thống chỉ được phép có đúng một tài khoản Publisher đang hoạt động (Q12). Hãy vô hiệu hóa hoặc đổi vai trò tài khoản Publisher hiện tại trước.";

    public static Task<bool> HasAnotherActivePublisherAsync(
        IPanelForgeDbContext dbContext, Guid? excludeUserId, CancellationToken cancellationToken) =>
        dbContext.Users.AsNoTracking().AnyAsync(
            u => u.Role == SystemRole.Publisher
                 && u.IsActive
                 && (!excludeUserId.HasValue || u.Id != excludeUserId.Value),
            cancellationToken);
}
