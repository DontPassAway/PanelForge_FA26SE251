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

/// <summary>
/// Admin đầu tiên được tạo thẳng trong DB; từ đó Admin tạo/cấp quyền Admin khác trong app.
/// Không cho hạ quyền hoặc khoá Administrator đang hoạt động cuối cùng, tránh hệ thống mất hết quyền quản trị.
/// </summary>
internal static class LastAdminGuard
{
    public const string ConflictMessage =
        "Đây là tài khoản Administrator đang hoạt động cuối cùng. Hãy cấp quyền Administrator cho một tài khoản khác trước khi hạ quyền hoặc khoá tài khoản này.";

    /// <summary>True khi user là Admin đang hoạt động và không còn Admin đang hoạt động nào khác.</summary>
    public static async Task<bool> IsLastActiveAdminAsync(
        IPanelForgeDbContext dbContext, User user, CancellationToken cancellationToken) =>
        user.Role == SystemRole.Admin
        && user.IsActive
        && !await dbContext.Users.AsNoTracking().AnyAsync(
            u => u.Role == SystemRole.Admin && u.IsActive && u.Id != user.Id,
            cancellationToken);
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
