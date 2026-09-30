using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Workspaces.Common;
using PanelForge.Application.Features.Workspaces.Models;

namespace PanelForge.Application.Features.Workspaces.Commands.ResendInvitation;

/// <summary>
/// POST /api/workspaces/{id}/members/{userId}/resend-invitation — Owner / Producer gửi lại OTP kích hoạt
/// cho thành viên được mời nhưng chưa kích hoạt tài khoản (OTP cũ hết hạn hoặc mail không tới).
/// </summary>
public sealed record ResendWorkspaceInvitationCommand(Guid ActorId, Guid WorkspaceId, Guid TargetUserId)
    : IRequest<Result<WorkspaceMemberDto>>;

public sealed class ResendWorkspaceInvitationCommandHandler
    : IRequestHandler<ResendWorkspaceInvitationCommand, Result<WorkspaceMemberDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IWorkspaceAuthorizationService _authorizationService;
    private readonly IEmailService _emailService;
    private readonly ILogger<ResendWorkspaceInvitationCommandHandler> _logger;

    public ResendWorkspaceInvitationCommandHandler(
        IPanelForgeDbContext dbContext,
        IWorkspaceAuthorizationService authorizationService,
        IEmailService emailService,
        ILogger<ResendWorkspaceInvitationCommandHandler> logger)
    {
        _dbContext = dbContext;
        _authorizationService = authorizationService;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<Result<WorkspaceMemberDto>> Handle(ResendWorkspaceInvitationCommand command, CancellationToken cancellationToken)
    {
        if (!await _authorizationService.IsOwnerOrProducerAsync(command.ActorId, command.WorkspaceId, cancellationToken))
            return Result<WorkspaceMemberDto>.Failure(
                "Chỉ Chủ sở hữu (Owner) hoặc Quản lý sản xuất (Producer) mới có quyền gửi lại lời mời.",
                ResultErrorCodes.Forbidden);

        var workspace = await _dbContext.StudioWorkspaces
            .FirstOrDefaultAsync(w => w.Id == command.WorkspaceId, cancellationToken);
        if (workspace is null)
            return Result<WorkspaceMemberDto>.Failure("Workspace không tồn tại.", ResultErrorCodes.NotFound);

        var member = await _dbContext.WorkspaceMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.WorkspaceId == command.WorkspaceId && m.UserId == command.TargetUserId, cancellationToken);
        if (member is null)
            return Result<WorkspaceMemberDto>.Failure("Thành viên không tồn tại trong Workspace này.", ResultErrorCodes.NotFound);

        var user = member.User;
        if (!user.IsActive)
            return Result<WorkspaceMemberDto>.Failure("Tài khoản của thành viên này hiện đang bị vô hiệu hóa.");
        if (!user.IsPendingInvitation)
            return Result<WorkspaceMemberDto>.Failure("Thành viên này đã kích hoạt tài khoản, không cần gửi lại lời mời.");

        var otp = WorkspaceInvitation.IssueOtp(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await _emailService.SendWorkspaceInvitationAsync(
                user.Email, user.FullName, workspace.Name, member.Role.ToString(), otp, WorkspaceInvitation.OtpLifetime, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không gửi lại được email mời tới {Email}", user.Email);
            return Result<WorkspaceMemberDto>.Failure("Không gửi được email mời. Vui lòng kiểm tra cấu hình email và thử lại.");
        }

        return Result<WorkspaceMemberDto>.Success(
            WorkspaceMapper.ToMemberDto(member, user, workspace.OwnerId) with { InvitationEmailSent = true });
    }
}
