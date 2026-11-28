using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Workspaces.Common;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Workspaces.Commands.ResendInvitation;

/// <summary>
/// POST /api/workspaces/{id}/members/{userId}/resend-invitation — Owner / Producer gửi lại email mời
/// cho thành viên chưa chấp nhận lời mời (Pending) hoặc chưa kích hoạt tài khoản (OTP cũ hết hạn, mail không tới).
/// Tài khoản chưa kích hoạt nhận OTP mới; mail không gửi được → EMAIL_DELIVERY_FAILED (503).
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
        if (!user.IsPendingInvitation && member.Status != WorkspaceMemberStatus.Pending)
            return Result<WorkspaceMemberDto>.Failure("Thành viên này đã tham gia Studio, không cần gửi lại lời mời.");

        string? otp = null;
        if (user.IsPendingInvitation)
        {
            otp = WorkspaceInvitation.IssueOtp(user);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var sent = await _emailService.SendWorkspaceInvitationAsync(
            user.Email, user.FullName, workspace.Name, member.Role.ToString(), otp, WorkspaceInvitation.OtpLifetime, cancellationToken);
        if (!sent)
        {
            _logger.LogWarning("Không gửi lại được email mời tới {Email}", user.Email);
            return Result<WorkspaceMemberDto>.Failure(
                "Không gửi được email mời. Vui lòng kiểm tra cấu hình email của hệ thống và thử lại.",
                ResultErrorCodes.EmailDeliveryFailed);
        }

        return Result<WorkspaceMemberDto>.Success(
            WorkspaceMapper.ToMemberDto(member, user, workspace.OwnerId) with { InvitationEmailSent = true });
    }
}
