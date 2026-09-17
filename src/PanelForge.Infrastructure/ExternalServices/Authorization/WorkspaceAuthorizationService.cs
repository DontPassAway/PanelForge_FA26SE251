using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Interfaces;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Services;

public class WorkspaceAuthorizationService : IWorkspaceAuthorizationService
{
    private readonly IPanelForgeDbContext _dbContext;

    public WorkspaceAuthorizationService(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> IsOwnerAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user?.Role == SystemRole.Admin) return true;

        var workspace = await _dbContext.StudioWorkspaces.FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);
        return workspace != null && workspace.OwnerId == userId;
    }

    public async Task<bool> IsOwnerOrProducerAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user?.Role == SystemRole.Admin) return true;

        var workspace = await _dbContext.StudioWorkspaces.FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);
        if (workspace == null) return false;
        if (workspace.OwnerId == userId) return true;

        var member = await _dbContext.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, cancellationToken);

        return member?.Role == WorkspaceRole.Producer;
    }

    public async Task<bool> HasWorkspaceRoleAsync(Guid userId, Guid workspaceId, WorkspaceRole[] allowedRoles, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user?.Role == SystemRole.Admin) return true;

        var workspace = await _dbContext.StudioWorkspaces.FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);
        if (workspace == null) return false;

        // Chủ sở hữu luôn có toàn quyền cao nhất
        if (workspace.OwnerId == userId) return true;

        var member = await _dbContext.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, cancellationToken);

        if (member == null) return false;

        return allowedRoles.Contains(member.Role);
    }

    public async Task<bool> IsMemberAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user?.Role == SystemRole.Admin) return true;

        var workspace = await _dbContext.StudioWorkspaces.FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);
        if (workspace == null) return false;
        if (workspace.OwnerId == userId) return true;

        return await _dbContext.WorkspaceMembers
            .AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, cancellationToken);
    }

    public async Task<WorkspaceRole?> GetMemberRoleAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var workspace = await _dbContext.StudioWorkspaces.FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);
        if (workspace == null) return null;

        if (workspace.OwnerId == userId)
        {
            return WorkspaceRole.Producer;
        }

        var member = await _dbContext.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, cancellationToken);

        return member?.Role;
    }
}
