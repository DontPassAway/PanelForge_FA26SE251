using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.SeriesMembers.Commands.RemoveSeriesMember;

/// <summary>
/// DELETE /api/series/{seriesId}/members/{userId} — Xóa nhân sự khỏi Series.
/// </summary>
public sealed record RemoveSeriesMemberCommand(
    Guid ActorId,
    Guid SeriesId,
    Guid TargetUserId) : IRequest<Result<bool>>;

public sealed class RemoveSeriesMemberCommandHandler
    : IRequestHandler<RemoveSeriesMemberCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IWorkspaceAuthorizationService _authorizationService;

    public RemoveSeriesMemberCommandHandler(
        IPanelForgeDbContext dbContext,
        IWorkspaceAuthorizationService authorizationService)
    {
        _dbContext = dbContext;
        _authorizationService = authorizationService;
    }

    public async Task<Result<bool>> Handle(
        RemoveSeriesMemberCommand command,
        CancellationToken cancellationToken)
    {
        var series = await _dbContext.Series
            .FirstOrDefaultAsync(s => s.Id == command.SeriesId, cancellationToken);

        if (series is null)
            return Result<bool>.Failure("Series không tồn tại.", ResultErrorCodes.NotFound);

        // Chỉ Owner hoặc Producer của Workspace mới có quyền xóa nhân sự
        if (!await _authorizationService.IsOwnerOrProducerAsync(command.ActorId, series.WorkspaceId, cancellationToken))
            return Result<bool>.Failure(
                "Chỉ Chủ sở hữu (Owner) hoặc Quản lý sản xuất (Producer) mới có quyền xóa nhân sự khỏi dự án.",
                ResultErrorCodes.Forbidden);

        // Producer hoặc Owner của Workspace luôn mặc định quản trị tất cả các Series trong Workspace
        var isTargetProducerOrOwner = await _authorizationService.IsOwnerOrProducerAsync(command.TargetUserId, series.WorkspaceId, cancellationToken);

        if (isTargetProducerOrOwner)
        {
            return Result<bool>.Failure(
                "Producer và Owner của Workspace luôn mặc định là thành viên quản trị Series và không thể bị xóa khỏi bộ truyện.",
                ResultErrorCodes.Conflict);
        }

        var member = await _dbContext.SeriesMembers
            .FirstOrDefaultAsync(sm => sm.SeriesId == command.SeriesId && sm.UserId == command.TargetUserId, cancellationToken);

        if (member is null)
            return Result<bool>.Failure("Thành viên này không có trong đội ngũ của Series.", ResultErrorCodes.NotFound);

        // Kiểm tra xem thành viên có công việc nào chưa hoàn tất trong Series không
        var hasActiveAssignments = await _dbContext.Assignments
            .AnyAsync(a => a.SeriesId == command.SeriesId &&
                           a.AssigneeUserId == command.TargetUserId &&
                           a.Status != AssignmentStatus.Approved, cancellationToken);

        if (hasActiveAssignments)
        {
            return Result<bool>.Failure(
                "Thành viên này đang có công việc chưa hoàn tất trong Series. Vui lòng chuyển giao công việc trước khi rút khỏi đội ngũ.",
                ResultErrorCodes.Conflict);
        }

        _dbContext.SeriesMembers.Remove(member);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<bool>.Success(true);
    }
}
