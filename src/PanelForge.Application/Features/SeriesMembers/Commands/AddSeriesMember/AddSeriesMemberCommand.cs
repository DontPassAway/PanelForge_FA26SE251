using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.SeriesMembers.Models;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.SeriesMembers.Commands.AddSeriesMember;

/// <summary>
/// POST /api/series/{seriesId}/members — Thêm nhân sự từ Workspace vào Series và gán vai trò.
/// </summary>
public sealed record AddSeriesMemberCommand(
    Guid ActorId,
    Guid SeriesId,
    Guid TargetUserId,
    WorkspaceRole Role) : IRequest<Result<SeriesMemberDto>>;

public sealed class AddSeriesMemberCommandHandler
    : IRequestHandler<AddSeriesMemberCommand, Result<SeriesMemberDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IWorkspaceAuthorizationService _authorizationService;

    public AddSeriesMemberCommandHandler(
        IPanelForgeDbContext dbContext,
        IWorkspaceAuthorizationService authorizationService)
    {
        _dbContext = dbContext;
        _authorizationService = authorizationService;
    }

    public async Task<Result<SeriesMemberDto>> Handle(
        AddSeriesMemberCommand command,
        CancellationToken cancellationToken)
    {
        var series = await _dbContext.Series
            .FirstOrDefaultAsync(s => s.Id == command.SeriesId, cancellationToken);

        if (series is null)
            return Result<SeriesMemberDto>.Failure("Series không tồn tại.", ResultErrorCodes.NotFound);

        // Chỉ Owner hoặc Producer của Workspace mới có quyền chỉ định nhân sự vào Series
        if (!await _authorizationService.IsOwnerOrProducerAsync(command.ActorId, series.WorkspaceId, cancellationToken))
            return Result<SeriesMemberDto>.Failure(
                "Chỉ Chủ sở hữu (Owner) hoặc Quản lý sản xuất (Producer) mới có quyền chỉ định nhân sự vào dự án.",
                ResultErrorCodes.Forbidden);

        if (!Enum.IsDefined(command.Role))
            return Result<SeriesMemberDto>.Failure(
                $"Vai trò không hợp lệ. Các vai trò hợp lệ: {string.Join(", ", Enum.GetNames<WorkspaceRole>())}.");

        // Xác thực người được thêm bắt buộc phải là thành viên Active hoặc Owner của Studio Workspace
        var isWorkspaceMember = await _dbContext.WorkspaceMembers
            .AnyAsync(wm => wm.WorkspaceId == series.WorkspaceId &&
                            wm.UserId == command.TargetUserId &&
                            wm.Status == WorkspaceMemberStatus.Active, cancellationToken);

        var isWorkspaceOwner = await _dbContext.StudioWorkspaces
            .AnyAsync(w => w.Id == series.WorkspaceId && w.OwnerId == command.TargetUserId, cancellationToken);

        if (!isWorkspaceMember && !isWorkspaceOwner)
        {
            return Result<SeriesMemberDto>.Failure(
                "Người dùng này chưa phải là thành viên hợp lệ trong Studio Workspace. Vui lòng mời vào Studio trước.",
                ResultErrorCodes.Conflict);
        }

        // Kiểm tra xem đã có trong Series chưa
        var alreadyExists = await _dbContext.SeriesMembers
            .AnyAsync(sm => sm.SeriesId == command.SeriesId && sm.UserId == command.TargetUserId, cancellationToken);

        if (alreadyExists)
        {
            return Result<SeriesMemberDto>.Failure(
                "Thành viên này đã có trong đội ngũ của Series. Hãy đổi vai trò thay vì thêm mới.",
                ResultErrorCodes.Conflict);
        }

        var newMember = SeriesMember.Create(command.SeriesId, command.TargetUserId, command.Role);
        _dbContext.SeriesMembers.Add(newMember);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == command.TargetUserId, cancellationToken);

        if (user is null)
            return Result<SeriesMemberDto>.Failure("Không tìm thấy thông tin tài khoản người dùng.", ResultErrorCodes.NotFound);

        return Result<SeriesMemberDto>.Success(new SeriesMemberDto(
            newMember.Id,
            newMember.SeriesId,
            user.Id,
            user.FullName,
            user.Email,
            user.AvatarUrl,
            newMember.Role,
            newMember.AssignedAt
        ));
    }
}
