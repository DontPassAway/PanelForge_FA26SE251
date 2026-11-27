using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Application.Features.Workspaces.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Workspaces.Commands.UpdateMemberRole;

/// <summary>PUT /api/workspaces/{id}/members/{userId} — đổi WorkspaceRole (Owner / Producer, không áp dụng cho Owner).</summary>
public sealed record UpdateMemberRoleCommand(Guid ActorId, Guid WorkspaceId, Guid TargetUserId, WorkspaceRole NewRole)
    : IRequest<Result<WorkspaceMemberDto>>;

public sealed class UpdateMemberRoleCommandHandler : IRequestHandler<UpdateMemberRoleCommand, Result<WorkspaceMemberDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IWorkspaceAuthorizationService _authorizationService;

    public UpdateMemberRoleCommandHandler(IPanelForgeDbContext dbContext, IWorkspaceAuthorizationService authorizationService)
    {
        _dbContext = dbContext;
        _authorizationService = authorizationService;
    }

    public async Task<Result<WorkspaceMemberDto>> Handle(UpdateMemberRoleCommand command, CancellationToken cancellationToken)
    {
        if (!await _authorizationService.IsOwnerOrProducerAsync(command.ActorId, command.WorkspaceId, cancellationToken))
            return Result<WorkspaceMemberDto>.Failure(
                "Chỉ Chủ sở hữu (Owner) hoặc Quản lý sản xuất (Producer) mới có quyền đổi vai trò thành viên.",
                ResultErrorCodes.Forbidden);

        if (!Enum.IsDefined(command.NewRole))
            return Result<WorkspaceMemberDto>.Failure(
                $"Vai trò không hợp lệ. Các vai trò hợp lệ: {string.Join(", ", Enum.GetNames<WorkspaceRole>())}.");

        // Spec D10: ở cấp Studio chỉ có Producer hoặc Member; vai trò chuyên môn gán khi thêm vào Series
        if (command.NewRole is not (WorkspaceRole.Producer or WorkspaceRole.Member))
            return Result<WorkspaceMemberDto>.Failure(
                "Vai trò trong Studio chỉ là Producer hoặc Member. Vai trò Writer/Artist/Letterer/Editor gán khi thêm vào từng Series.",
                ResultErrorCodes.Validation);

        var workspace = await _dbContext.StudioWorkspaces
            .FirstOrDefaultAsync(w => w.Id == command.WorkspaceId, cancellationToken);
        if (workspace is null)
            return Result<WorkspaceMemberDto>.Failure("Workspace không tồn tại.", ResultErrorCodes.NotFound);

        if (command.TargetUserId == workspace.OwnerId)
            return Result<WorkspaceMemberDto>.Failure("Không thể thay đổi vai trò của Chủ sở hữu (Owner) Workspace.");

        var member = await _dbContext.WorkspaceMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.WorkspaceId == command.WorkspaceId && m.UserId == command.TargetUserId, cancellationToken);
        if (member is null)
            return Result<WorkspaceMemberDto>.Failure("Thành viên không tồn tại trong Workspace này.", ResultErrorCodes.NotFound);

        var seriesIds = await _dbContext.Series
            .Where(s => s.WorkspaceId == command.WorkspaceId).Select(s => s.Id).ToListAsync(cancellationToken);

        // Việc đang mở theo vai trò Producer: hạ xuống Member lúc này để lại việc sai vai trò (vai trò Series không đổi, D10)
        if (member.Role == WorkspaceRole.Producer && command.NewRole != WorkspaceRole.Producer
            && await Assignments.Common.AssignmentRules.OpenWorkCountAsync(_dbContext, command.TargetUserId, seriesIds, cancellationToken)
                is var open and > 0)
            return Result<WorkspaceMemberDto>.Failure(
                $"Thành viên còn {open} việc chưa xong theo vai trò {member.Role}. Hãy giao lại hoặc hoàn tất các việc đó trước khi đổi vai trò.",
                ResultErrorCodes.Conflict);

        if (member.Role == WorkspaceRole.Editor && command.NewRole != WorkspaceRole.Editor)
            await TaskGrid.Common.TaskGridRules.ReleaseReviewerAsync(_dbContext, command.TargetUserId, seriesIds, cancellationToken);

        member.ChangeRole(command.NewRole);

        // D10: vai trò Series độc lập với vai trò Studio, không đồng bộ ở đây

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<WorkspaceMemberDto>.Success(WorkspaceMapper.ToMemberDto(member, member.User, workspace.OwnerId));
    }
}
