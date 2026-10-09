using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Workspaces.Models;

namespace PanelForge.Application.Features.Workspaces.Queries.GetWorkspaceSessionPolicy;

public sealed record GetWorkspaceSessionPolicyQuery(Guid WorkspaceId) : IRequest<Result<WorkspaceSessionPolicyDto>>;

public sealed class GetWorkspaceSessionPolicyQueryHandler
    : IRequestHandler<GetWorkspaceSessionPolicyQuery, Result<WorkspaceSessionPolicyDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetWorkspaceSessionPolicyQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<WorkspaceSessionPolicyDto>> Handle(GetWorkspaceSessionPolicyQuery request, CancellationToken cancellationToken)
    {
        var policy = await _dbContext.StudioWorkspaces.AsNoTracking()
            .Where(w => w.Id == request.WorkspaceId)
            .Select(w => new WorkspaceSessionPolicyDto(
                w.Id, w.SessionLifetimeMinutes, w.SessionIdleTimeoutMinutes, w.MaxConcurrentSessions))
            .FirstOrDefaultAsync(cancellationToken);

        return policy is null
            ? Result<WorkspaceSessionPolicyDto>.Failure($"Workspace {request.WorkspaceId} không tồn tại.", ResultErrorCodes.NotFound)
            : Result<WorkspaceSessionPolicyDto>.Success(policy);
    }
}
