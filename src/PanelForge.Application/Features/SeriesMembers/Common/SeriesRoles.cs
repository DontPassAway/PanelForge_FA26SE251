using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.SeriesMembers.Common;

/// <summary>
/// Spec D10: vai trò chuyên môn (Writer/Artist/Letterer/Editor) gán theo Series; Studio chỉ có Producer hoặc Member.
/// Người giữ vai trò trong Series = dòng <c>series_members</c> chưa xoá mềm và còn là thành viên Active của Workspace.
/// </summary>
public static class SeriesRoles
{
    public static IQueryable<SeriesMember> Holders(IPanelForgeDbContext db, Guid workspaceId, Guid seriesId) =>
        db.SeriesMembers.AsNoTracking().Where(sm =>
            sm.SeriesId == seriesId && !sm.IsDeleted &&
            db.WorkspaceMembers.Any(m => m.WorkspaceId == workspaceId && m.UserId == sm.UserId
                                         && m.Status == WorkspaceMemberStatus.Active));

    public static Task<List<Guid>> UserIdsWithRoleAsync(
        IPanelForgeDbContext db, Guid workspaceId, Guid seriesId, WorkspaceRole role, CancellationToken ct) =>
        Holders(db, workspaceId, seriesId).Where(sm => sm.Role == role).Select(sm => sm.UserId).ToListAsync(ct);

    public static Task<WorkspaceRole?> RoleOfAsync(
        IPanelForgeDbContext db, Guid workspaceId, Guid seriesId, Guid userId, CancellationToken ct) =>
        Holders(db, workspaceId, seriesId).Where(sm => sm.UserId == userId)
            .Select(sm => (WorkspaceRole?)sm.Role).FirstOrDefaultAsync(ct);

    /// <summary>Vai trò của từng người trong Series (dùng thay map vai trò Workspace khi chọn người nhận việc).</summary>
    public static Task<Dictionary<Guid, WorkspaceRole>> RoleMapAsync(
        IPanelForgeDbContext db, Guid workspaceId, Guid seriesId, CancellationToken ct) =>
        Holders(db, workspaceId, seriesId).ToDictionaryAsync(sm => sm.UserId, sm => sm.Role, ct);

    /// <summary>Map vai trò người có thể nhận việc trong Series: vai trò Series, cộng Producer của Studio (giữ vai trò Producer).</summary>
    public static async Task<Dictionary<Guid, WorkspaceRole>> AssignableRoleMapAsync(
        IPanelForgeDbContext db, Guid workspaceId, Guid seriesId, CancellationToken ct)
    {
        var map = await RoleMapAsync(db, workspaceId, seriesId, ct);
        foreach (var producerId in await StudioProducerIdsAsync(db, workspaceId, ct))
            map.TryAdd(producerId, WorkspaceRole.Producer);
        return map;
    }

    /// <summary>Producer của Studio (vai trò Producer ở cấp Studio, D10).</summary>
    public static Task<List<Guid>> StudioProducerIdsAsync(IPanelForgeDbContext db, Guid workspaceId, CancellationToken ct) =>
        db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && m.Role == WorkspaceRole.Producer && m.Status == WorkspaceMemberStatus.Active)
            .Select(m => m.UserId).ToListAsync(ct);
}
