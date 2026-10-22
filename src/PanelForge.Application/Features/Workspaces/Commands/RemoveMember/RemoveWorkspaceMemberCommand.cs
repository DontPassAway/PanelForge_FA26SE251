using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Workspaces.Commands.RemoveMember;

/// <summary>
/// DELETE /api/workspaces/{id}/members/{userId} — Owner / Producer xóa thành viên,
/// hoặc thành viên tự rời Studio. Không xóa được Owner.
/// </summary>
public sealed record RemoveWorkspaceMemberCommand(Guid ActorId, Guid WorkspaceId, Guid TargetUserId) : IRequest<Result<bool>>;

public sealed class RemoveWorkspaceMemberCommandHandler : IRequestHandler<RemoveWorkspaceMemberCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IWorkspaceAuthorizationService _authorizationService;

    public RemoveWorkspaceMemberCommandHandler(IPanelForgeDbContext dbContext, IWorkspaceAuthorizationService authorizationService)
    {
        _dbContext = dbContext;
        _authorizationService = authorizationService;
    }

    public async Task<Result<bool>> Handle(RemoveWorkspaceMemberCommand command, CancellationToken cancellationToken)
    {
        var workspace = await _dbContext.StudioWorkspaces
            .FirstOrDefaultAsync(w => w.Id == command.WorkspaceId, cancellationToken);
        if (workspace is null)
            return Result<bool>.Failure("Workspace không tồn tại.", ResultErrorCodes.NotFound);

        if (command.TargetUserId == workspace.OwnerId)
            return Result<bool>.Failure("Không thể xóa Chủ sở hữu (Owner) khỏi Workspace.");

        var isSelfLeaving = command.ActorId == command.TargetUserId;
        if (!isSelfLeaving
            && !await _authorizationService.IsOwnerOrProducerAsync(command.ActorId, command.WorkspaceId, cancellationToken))
            return Result<bool>.Failure("Bạn không có quyền xóa thành viên này khỏi Workspace.", ResultErrorCodes.Forbidden);

        var member = await _dbContext.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == command.WorkspaceId && m.UserId == command.TargetUserId, cancellationToken);
        if (member is null)
            return Result<bool>.Failure("Thành viên không tồn tại trong Workspace này.", ResultErrorCodes.NotFound);

        _dbContext.WorkspaceMembers.Remove(member);

        // Đồng thời rút thành viên khỏi các Series thuộc Workspace này
        var seriesIds = await _dbContext.Series
            .Where(s => s.WorkspaceId == command.WorkspaceId)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (seriesIds.Count > 0)
        {
            var seriesMembers = await _dbContext.SeriesMembers
                .Where(sm => seriesIds.Contains(sm.SeriesId) && sm.UserId == command.TargetUserId)
                .ToListAsync(cancellationToken);

            if (seriesMembers.Count > 0)
            {
                _dbContext.SeriesMembers.RemoveRange(seriesMembers);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<bool>.Success(true);
    }
}
