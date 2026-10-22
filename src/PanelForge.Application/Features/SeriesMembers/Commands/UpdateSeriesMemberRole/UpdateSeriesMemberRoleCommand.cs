using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.SeriesMembers.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.SeriesMembers.Commands.UpdateSeriesMemberRole;

/// <summary>
/// PUT /api/series/{seriesId}/members/{userId} — Thay đổi vai trò của thành viên trong Series.
/// </summary>
public sealed record UpdateSeriesMemberRoleCommand(
    Guid ActorId,
    Guid SeriesId,
    Guid TargetUserId,
    WorkspaceRole NewRole) : IRequest<Result<SeriesMemberDto>>;

public sealed class UpdateSeriesMemberRoleCommandHandler
    : IRequestHandler<UpdateSeriesMemberRoleCommand, Result<SeriesMemberDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IWorkspaceAuthorizationService _authorizationService;

    public UpdateSeriesMemberRoleCommandHandler(
        IPanelForgeDbContext dbContext,
        IWorkspaceAuthorizationService authorizationService)
    {
        _dbContext = dbContext;
        _authorizationService = authorizationService;
    }

    public async Task<Result<SeriesMemberDto>> Handle(
        UpdateSeriesMemberRoleCommand command,
        CancellationToken cancellationToken)
    {
        var series = await _dbContext.Series
            .FirstOrDefaultAsync(s => s.Id == command.SeriesId, cancellationToken);

        if (series is null)
            return Result<SeriesMemberDto>.Failure("Series không tồn tại.", ResultErrorCodes.NotFound);

        // Chỉ Owner hoặc Producer của Workspace mới có quyền sửa vai trò
        if (!await _authorizationService.IsOwnerOrProducerAsync(command.ActorId, series.WorkspaceId, cancellationToken))
            return Result<SeriesMemberDto>.Failure(
                "Chỉ Chủ sở hữu (Owner) hoặc Quản lý sản xuất (Producer) mới có quyền đổi vai trò nhân sự trong dự án.",
                ResultErrorCodes.Forbidden);

        if (!Enum.IsDefined(command.NewRole))
            return Result<SeriesMemberDto>.Failure(
                $"Vai trò không hợp lệ. Các vai trò hợp lệ: {string.Join(", ", Enum.GetNames<WorkspaceRole>())}.");

        var member = await _dbContext.SeriesMembers
            .Include(sm => sm.User)
            .FirstOrDefaultAsync(sm => sm.SeriesId == command.SeriesId && sm.UserId == command.TargetUserId, cancellationToken);

        if (member is null)
            return Result<SeriesMemberDto>.Failure("Thành viên này không có trong đội ngũ của Series.", ResultErrorCodes.NotFound);

        member.ChangeRole(command.NewRole);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<SeriesMemberDto>.Success(new SeriesMemberDto(
            member.Id,
            member.SeriesId,
            member.UserId,
            member.User.FullName,
            member.User.Email,
            member.User.AvatarUrl,
            member.Role,
            member.AssignedAt
        ));
    }
}
