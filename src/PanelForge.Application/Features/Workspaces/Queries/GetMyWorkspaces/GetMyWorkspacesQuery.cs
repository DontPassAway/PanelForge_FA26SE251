using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Application.Features.Workspaces.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Workspaces.Queries.GetMyWorkspaces;

/// <summary>GET /api/workspaces — các Studio mà user sở hữu hoặc là thành viên (BR-23), kèm số task chưa xong.</summary>
public sealed record GetMyWorkspacesQuery(Guid UserId) : IRequest<Result<IReadOnlyList<WorkspaceDto>>>;

public sealed class GetMyWorkspacesQueryHandler
    : IRequestHandler<GetMyWorkspacesQuery, Result<IReadOnlyList<WorkspaceDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetMyWorkspacesQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<IReadOnlyList<WorkspaceDto>>> Handle(GetMyWorkspacesQuery query, CancellationToken cancellationToken)
    {
        var userId = query.UserId;

        var workspaces = await _dbContext.StudioWorkspaces
            .Include(w => w.Owner)
            .Include(w => w.Members.Where(m => !m.IsDeleted))
            .Where(w => !w.IsDeleted && (w.OwnerId == userId || w.Members.Any(m => m.UserId == userId && !m.IsDeleted)))
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(cancellationToken);

        var workspaceIds = workspaces.Select(w => w.Id).ToList();
        var pendingByWorkspace = await _dbContext.Assignments
            .Where(a => !a.IsDeleted && a.AssigneeUserId == userId && a.Status != AssignmentStatus.Approved
                        && workspaceIds.Contains(a.Series.WorkspaceId))
            .GroupBy(a => a.Series.WorkspaceId)
            .Select(g => new { WorkspaceId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.WorkspaceId, x => x.Count, cancellationToken);

        IReadOnlyList<WorkspaceDto> result = workspaces
            .Select(w => WorkspaceMapper.ToDto(
                w,
                userId,
                memberCount: w.Members.Count(m => !m.IsDeleted),
                pendingTasks: pendingByWorkspace.GetValueOrDefault(w.Id)))
            .ToList();

        return Result<IReadOnlyList<WorkspaceDto>>.Success(result);
    }
}
