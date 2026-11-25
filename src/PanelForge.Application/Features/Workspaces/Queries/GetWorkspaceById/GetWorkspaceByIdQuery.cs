using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Application.Features.Workspaces.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Workspaces.Queries.GetWorkspaceById;

/// <summary>GET /api/workspaces/{id} — chỉ thành viên của Studio mới xem được.</summary>
public sealed record GetWorkspaceByIdQuery(Guid UserId, Guid WorkspaceId) : IRequest<Result<WorkspaceDto>>;

public sealed class GetWorkspaceByIdQueryHandler : IRequestHandler<GetWorkspaceByIdQuery, Result<WorkspaceDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IWorkspaceAuthorizationService _authorizationService;

    public GetWorkspaceByIdQueryHandler(IPanelForgeDbContext dbContext, IWorkspaceAuthorizationService authorizationService)
    {
        _dbContext = dbContext;
        _authorizationService = authorizationService;
    }

    public async Task<Result<WorkspaceDto>> Handle(GetWorkspaceByIdQuery query, CancellationToken cancellationToken)
    {
        var workspace = await _dbContext.StudioWorkspaces
            .Include(w => w.Owner)
            .Include(w => w.Members.Where(m => !m.IsDeleted))
            .FirstOrDefaultAsync(w => w.Id == query.WorkspaceId && !w.IsDeleted, cancellationToken);
        if (workspace is null)
            return Result<WorkspaceDto>.Failure("Workspace không tồn tại.", ResultErrorCodes.NotFound);

        if (!await _authorizationService.IsMemberAsync(query.UserId, query.WorkspaceId, cancellationToken))
            return Result<WorkspaceDto>.Failure("Bạn không có quyền truy cập Workspace này.", ResultErrorCodes.Forbidden);

        var pendingTasks = await _dbContext.Assignments.CountAsync(
            a => !a.IsDeleted && a.AssigneeUserId == query.UserId && a.Status != AssignmentStatus.Approved
                 && a.Series.WorkspaceId == query.WorkspaceId,
            cancellationToken);

        var pendingEditorReviews = await _dbContext.Assignments.CountAsync(
            a => !a.IsDeleted
                 && (a.Status == AssignmentStatus.Submitted || a.Status == AssignmentStatus.UnderReview)
                 && a.AssigneeUserId != query.UserId
                 && a.Series.WorkspaceId == query.WorkspaceId,
            cancellationToken);

        return Result<WorkspaceDto>.Success(WorkspaceMapper.ToDto(
            workspace,
            query.UserId,
            workspace.Members.Count(m => !m.IsDeleted),
            pendingTasks,
            pendingEditorReviews: pendingEditorReviews));
    }
}
