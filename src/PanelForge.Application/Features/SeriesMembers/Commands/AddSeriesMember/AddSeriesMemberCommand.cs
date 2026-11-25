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

        // Xác thực người được thêm bắt buộc phải là thành viên Active hoặc Owner của Studio Workspace.
        // Role trong Series luôn = role Workspace (command.Role chỉ giữ cho tương thích request cũ, bị bỏ qua).
        var workspaceRole = await _dbContext.WorkspaceMembers
            .Where(wm => wm.WorkspaceId == series.WorkspaceId &&
                         wm.UserId == command.TargetUserId &&
                         wm.Status == WorkspaceMemberStatus.Active)
            .Select(wm => (WorkspaceRole?)wm.Role)
            .FirstOrDefaultAsync(cancellationToken);

        var isWorkspaceOwner = await _dbContext.StudioWorkspaces
            .AnyAsync(w => w.Id == series.WorkspaceId && w.OwnerId == command.TargetUserId, cancellationToken);
        var role = workspaceRole ?? WorkspaceRole.Producer; // Owner không có dòng member → coi như Producer

        if (workspaceRole is null && !isWorkspaceOwner)
        {
            return Result<SeriesMemberDto>.Failure(
                "Người dùng này chưa phải là thành viên hợp lệ trong Studio Workspace. Vui lòng mời vào Studio trước.",
                ResultErrorCodes.Conflict);
        }

        // Kiểm tra xem đã từng tồn tại trong Series chưa (kể cả đã bị xóa mềm)
        var existingMember = await _dbContext.SeriesMembers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(sm => sm.SeriesId == command.SeriesId && sm.UserId == command.TargetUserId, cancellationToken);

        SeriesMember memberToSave;

        if (existingMember is not null)
        {
            if (!existingMember.IsDeleted)
            {
                return Result<SeriesMemberDto>.Failure(
                    "Thành viên này đã có trong đội ngũ của Series. Hãy đổi vai trò thay vì thêm mới.",
                    ResultErrorCodes.Conflict);
            }

            // Đã từng bị xóa mềm -> Khôi phục lại (Restore) với vai trò mới
            existingMember.IsDeleted = false;
            existingMember.DeletedAt = null;
            existingMember.DeletedBy = null;
            existingMember.ChangeRole(role);
            memberToSave = existingMember;
        }
        else
        {
            memberToSave = SeriesMember.Create(command.SeriesId, command.TargetUserId, role);
            _dbContext.SeriesMembers.Add(memberToSave);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == command.TargetUserId, cancellationToken);

        if (user is null)
            return Result<SeriesMemberDto>.Failure("Không tìm thấy thông tin tài khoản người dùng.", ResultErrorCodes.NotFound);

        return Result<SeriesMemberDto>.Success(new SeriesMemberDto(
            memberToSave.Id,
            memberToSave.SeriesId,
            user.Id,
            user.FullName,
            user.Email,
            user.AvatarUrl,
            memberToSave.Role,
            memberToSave.AssignedAt
        ));
    }
}
