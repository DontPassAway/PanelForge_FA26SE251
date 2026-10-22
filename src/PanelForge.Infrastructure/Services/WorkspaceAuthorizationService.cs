using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Services;

/// <summary>
/// Kiểm tra quyền cấp Workspace (BR-07 two-level access control).
/// Administrator KHÔNG có WorkspaceRole và KHÔNG có quyền nội dung trong bất kỳ Studio nào,
/// nên mọi kiểm tra ở đây đều trả về false cho Admin. Chức năng quản trị đi qua AdminController.
/// </summary>
public class WorkspaceAuthorizationService : IWorkspaceAuthorizationService
{
    private readonly IPanelForgeDbContext _dbContext;

    public WorkspaceAuthorizationService(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> IsOwnerAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        if (await IsAdminAsync(userId, cancellationToken)) return false;

        var workspace = await _dbContext.StudioWorkspaces.FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);
        return workspace != null && workspace.OwnerId == userId;
    }

    public async Task<bool> IsOwnerOrProducerAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        if (await IsAdminAsync(userId, cancellationToken)) return false;

        var workspace = await _dbContext.StudioWorkspaces.FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);
        if (workspace == null) return false;
        if (workspace.OwnerId == userId) return true;

        var member = await _dbContext.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, cancellationToken);

        return member?.Role == WorkspaceRole.Producer;
    }

    public async Task<bool> HasWorkspaceRoleAsync(Guid userId, Guid workspaceId, WorkspaceRole[] allowedRoles, CancellationToken cancellationToken = default)
    {
        if (await IsAdminAsync(userId, cancellationToken)) return false;

        var workspace = await _dbContext.StudioWorkspaces.FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);
        if (workspace == null) return false;

        // Chủ sở hữu luôn có toàn quyền cao nhất
        if (workspace.OwnerId == userId) return true;

        var member = await _dbContext.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, cancellationToken);

        if (member == null || member.Status != WorkspaceMemberStatus.Active) return false;

        return allowedRoles.Contains(member.Role);
    }

    public async Task<bool> IsMemberAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        if (await IsAdminAsync(userId, cancellationToken)) return false;

        var workspace = await _dbContext.StudioWorkspaces.FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);
        if (workspace == null) return false;
        if (workspace.OwnerId == userId) return true;

        return await _dbContext.WorkspaceMembers
            .AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId && m.Status == WorkspaceMemberStatus.Active, cancellationToken);
    }

    public async Task<WorkspaceRole?> GetMemberRoleAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        if (await IsAdminAsync(userId, cancellationToken)) return null;

        var workspace = await _dbContext.StudioWorkspaces.FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);
        if (workspace == null) return null;

        if (workspace.OwnerId == userId)
        {
            return WorkspaceRole.Producer;
        }

        var member = await _dbContext.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, cancellationToken);

        if (member == null || member.Status != WorkspaceMemberStatus.Active) return null;

        return member.Role;
    }

    public Task<Guid?> GetWorkspaceIdOfSeriesAsync(Guid seriesId, CancellationToken cancellationToken = default)
        => _dbContext.Series
            .Where(s => s.Id == seriesId)
            .Select(s => (Guid?)s.WorkspaceId)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Guid?> GetWorkspaceIdOfChapterAsync(Guid chapterId, CancellationToken cancellationToken = default)
        => _dbContext.Chapters
            .Where(c => c.Id == chapterId)
            .Select(c => (Guid?)c.Series.WorkspaceId)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Guid?> GetWorkspaceIdOfPageAsync(Guid pageId, CancellationToken cancellationToken = default)
        => _dbContext.Pages
            .Where(p => p.Id == pageId)
            .Select(p => (Guid?)p.Chapter.Series.WorkspaceId)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Guid?> GetWorkspaceIdOfPanelAsync(Guid panelId, CancellationToken cancellationToken = default)
        => _dbContext.Panels
            .Where(p => p.Id == panelId)
            .Select(p => (Guid?)p.Page.Chapter.Series.WorkspaceId)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Guid?> GetWorkspaceIdOfSceneAsync(Guid sceneId, CancellationToken cancellationToken = default)
        => _dbContext.Scenes
            .Where(s => s.Id == sceneId)
            .Select(s => (Guid?)s.Chapter.Series.WorkspaceId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> HasSeriesRoleAsync(Guid userId, Guid seriesId, WorkspaceRole[] allowedRoles, CancellationToken cancellationToken = default)
    {
        if (await IsAdminAsync(userId, cancellationToken)) return false;

        var workspaceId = await GetWorkspaceIdOfSeriesAsync(seriesId, cancellationToken);
        if (!workspaceId.HasValue) return false;

        // Workspace Owner hoặc Producer luôn có toàn quyền trên các Series của Workspace
        if (await IsOwnerOrProducerAsync(userId, workspaceId.Value, cancellationToken)) return true;

        // Thành viên Series được phân quyền chuyên môn cụ thể
        var member = await _dbContext.SeriesMembers
            .FirstOrDefaultAsync(sm => sm.SeriesId == seriesId && sm.UserId == userId, cancellationToken);

        return member != null && allowedRoles.Contains(member.Role);
    }

    public async Task<WorkspaceRole?> GetSeriesMemberRoleAsync(Guid userId, Guid seriesId, CancellationToken cancellationToken = default)
    {
        if (await IsAdminAsync(userId, cancellationToken)) return null;

        var workspaceId = await GetWorkspaceIdOfSeriesAsync(seriesId, cancellationToken);
        if (!workspaceId.HasValue) return null;

        var workspace = await _dbContext.StudioWorkspaces.FirstOrDefaultAsync(w => w.Id == workspaceId.Value, cancellationToken);
        if (workspace != null && workspace.OwnerId == userId)
        {
            return WorkspaceRole.Producer;
        }

        var seriesMember = await _dbContext.SeriesMembers
            .FirstOrDefaultAsync(sm => sm.SeriesId == seriesId && sm.UserId == userId, cancellationToken);

        if (seriesMember != null)
        {
            return seriesMember.Role;
        }

        var wsMember = await _dbContext.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId.Value && m.UserId == userId && m.Status == WorkspaceMemberStatus.Active, cancellationToken);

        if (wsMember?.Role == WorkspaceRole.Producer)
        {
            return WorkspaceRole.Producer;
        }

        return null;
    }

    // BR-07: Administrator không bao giờ có quyền cấp Workspace.
    private Task<bool> IsAdminAsync(Guid userId, CancellationToken cancellationToken)
        => _dbContext.Users.AnyAsync(u => u.Id == userId && u.Role == SystemRole.Admin, cancellationToken);
}
