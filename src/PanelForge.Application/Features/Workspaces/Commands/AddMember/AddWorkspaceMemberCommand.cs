using System.Security.Cryptography;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Application.Features.Workspaces.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Workspaces.Commands.AddMember;

/// <summary>
/// POST /api/workspaces/{id}/members — thêm thành viên theo email (Owner / Producer).
/// Email chưa có tài khoản → tạo tài khoản KHÔNG mật khẩu, người được mời đặt mật khẩu bằng OTP
/// (reset-password); đặt thành công cũng xác thực luôn email.
/// </summary>
public sealed record AddWorkspaceMemberCommand(Guid ActorId, Guid WorkspaceId, string Email, WorkspaceRole Role)
    : IRequest<Result<WorkspaceMemberDto>>;

public sealed class AddWorkspaceMemberCommandHandler
    : IRequestHandler<AddWorkspaceMemberCommand, Result<WorkspaceMemberDto>>
{
    // Cùng thời hạn với OTP quên mật khẩu
    private static readonly TimeSpan InviteOtpLifetime = TimeSpan.FromMinutes(15);

    private readonly IPanelForgeDbContext _dbContext;
    private readonly IWorkspaceAuthorizationService _authorizationService;
    private readonly IEmailService _emailService;
    private readonly ILogger<AddWorkspaceMemberCommandHandler> _logger;

    public AddWorkspaceMemberCommandHandler(
        IPanelForgeDbContext dbContext,
        IWorkspaceAuthorizationService authorizationService,
        IEmailService emailService,
        ILogger<AddWorkspaceMemberCommandHandler> logger)
    {
        _dbContext = dbContext;
        _authorizationService = authorizationService;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<Result<WorkspaceMemberDto>> Handle(AddWorkspaceMemberCommand command, CancellationToken cancellationToken)
    {
        if (!await _authorizationService.IsOwnerOrProducerAsync(command.ActorId, command.WorkspaceId, cancellationToken))
            return Result<WorkspaceMemberDto>.Failure(
                "Chỉ Chủ sở hữu (Owner) hoặc Quản lý sản xuất (Producer) mới có quyền thêm thành viên.",
                ResultErrorCodes.Forbidden);

        var workspace = await _dbContext.StudioWorkspaces
            .FirstOrDefaultAsync(w => w.Id == command.WorkspaceId, cancellationToken);
        if (workspace is null)
            return Result<WorkspaceMemberDto>.Failure("Workspace không tồn tại.", ResultErrorCodes.NotFound);

        var email = command.Email.Trim().ToLowerInvariant();
        var targetUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        string? inviteOtp = null;
        if (targetUser is null)
        {
            inviteOtp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            targetUser = User.Create(
                email: email,
                fullName: email.Split('@')[0],
                passwordHash: null,
                role: SystemRole.User);
            targetUser.SetPasswordResetToken(inviteOtp, DateTime.UtcNow.Add(InviteOtpLifetime));
            _dbContext.Users.Add(targetUser);
        }
        else
        {
            if (!targetUser.IsActive)
                return Result<WorkspaceMemberDto>.Failure(
                    $"Tài khoản người dùng với email '{command.Email}' hiện đang bị vô hiệu hóa.");

            // BR-07: Administrator không có dòng workspace_members ở bất kỳ Studio nào
            if (targetUser.Role == SystemRole.Admin)
                return Result<WorkspaceMemberDto>.Failure(
                    "Administrator không được phép tham gia Workspace với tư cách thành viên (BR-07).");

            var isAlreadyMember = await _dbContext.WorkspaceMembers
                .AnyAsync(m => m.WorkspaceId == command.WorkspaceId && m.UserId == targetUser.Id, cancellationToken);
            if (isAlreadyMember)
                return Result<WorkspaceMemberDto>.Failure("Người dùng này đã là thành viên của Workspace.");
        }

        var member = WorkspaceMember.Create(command.WorkspaceId, targetUser.Id, command.Role);
        _dbContext.WorkspaceMembers.Add(member);

        // Tạo user (nếu là lời mời) và membership trong cùng một lần lưu
        await _dbContext.SaveChangesAsync(cancellationToken);

        await NotifyAsync(targetUser, workspace.Name, member.Role, inviteOtp, cancellationToken);

        return Result<WorkspaceMemberDto>.Success(WorkspaceMapper.ToMemberDto(member, targetUser, workspace.OwnerId));
    }

    // Lỗi gửi email không làm hỏng thao tác thêm thành viên (đã lưu)
    private async Task NotifyAsync(User user, string workspaceName, WorkspaceRole role, string? inviteOtp, CancellationToken cancellationToken)
    {
        try
        {
            await _emailService.SendWorkspaceInvitationAsync(
                user.Email, user.FullName, workspaceName, role.ToString(), inviteOtp, InviteOtpLifetime, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không gửi được email mời thành viên tới {Email}", user.Email);
        }
    }
}
