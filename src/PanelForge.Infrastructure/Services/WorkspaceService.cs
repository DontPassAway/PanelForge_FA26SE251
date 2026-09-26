using System.Net;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PanelForge.Application.DTOs.Workspaces;
using PanelForge.Application.Interfaces;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Auth;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Services;

public class WorkspaceService : IWorkspaceService
{
    // Cùng thời hạn với OTP quên mật khẩu (AuthService.ForgotPasswordAsync)
    private static readonly TimeSpan InviteOtpLifetime = TimeSpan.FromMinutes(15);

    private readonly IPanelForgeDbContext _dbContext;
    private readonly IWorkspaceAuthorizationService _authorizationService;
    private readonly IEmailService _emailService;
    private readonly ILogger<WorkspaceService> _logger;

    public WorkspaceService(
        IPanelForgeDbContext dbContext,
        IWorkspaceAuthorizationService authorizationService,
        IEmailService emailService,
        ILogger<WorkspaceService> logger)
    {
        _dbContext = dbContext;
        _authorizationService = authorizationService;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<WorkspaceDto> CreateWorkspaceAsync(Guid ownerId, CreateWorkspaceRequest request, CancellationToken cancellationToken = default)
    {
        var owner = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == ownerId, cancellationToken);
        if (owner is null || !owner.IsActive)
        {
            throw new UnauthorizedAccessException("Người dùng không tồn tại hoặc đã bị vô hiệu hóa.");
        }

        // BR-07: Administrator không sở hữu / tham gia Studio.
        if (owner.Role == SystemRole.Admin)
        {
            throw new UnauthorizedAccessException("Administrator không được phép tạo Studio (BR-07).");
        }

        // BR-22: Chỉ User với CanCreateStudio = true mới được tạo Workspace.
        if (!owner.CanCreateStudio)
        {
            throw new UnauthorizedAccessException(
                "Bạn không có quyền tạo Studio. Vui lòng yêu cầu Administrator cấp quyền CanCreateStudio.");
        }

        var workspace = StudioWorkspace.Create(request.Name, ownerId, request.StorageQuotaBytes);
        _dbContext.StudioWorkspaces.Add(workspace);

        // Tự động thêm Owner làm thành viên với vai trò Producer (BR-22, atomically)
        var ownerMember = WorkspaceMember.Create(workspace.Id, ownerId, WorkspaceRole.Producer);
        _dbContext.WorkspaceMembers.Add(ownerMember);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new WorkspaceDto(
            Id: workspace.Id,
            Name: workspace.Name,
            OwnerId: owner.Id,
            OwnerName: owner.FullName,
            OwnerEmail: owner.Email,
            StorageQuotaBytes: workspace.StorageQuotaBytes,
            UsedStorageBytes: workspace.UsedStorageBytes,
            CreatedAt: workspace.CreatedAt,
            IsOwner: true,
            CurrentUserRole: WorkspaceRole.Producer,
            MemberCount: 1
        );
    }


    public async Task<IEnumerable<WorkspaceDto>> GetUserWorkspacesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var workspaces = await _dbContext.StudioWorkspaces
            .Include(w => w.Owner)
            .Include(w => w.Members.Where(m => !m.IsDeleted))
            .Where(w => !w.IsDeleted && (w.OwnerId == userId || w.Members.Any(m => m.UserId == userId && !m.IsDeleted)))
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(cancellationToken);

        var workspaceIds = workspaces.Select(w => w.Id).ToList();
        var pendingTasksCount = await _dbContext.Assignments
            .Where(a => !a.IsDeleted && a.AssigneeUserId == userId && a.Status != AssignmentStatus.Approved && workspaceIds.Contains(a.Series.WorkspaceId))
            .GroupBy(a => a.Series.WorkspaceId)
            .Select(g => new { WorkspaceId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.WorkspaceId, x => x.Count, cancellationToken);

        return workspaces.Select(w =>
        {
            var isOwner = w.OwnerId == userId;
            var currentMember = w.Members.FirstOrDefault(m => m.UserId == userId);
            var role = isOwner ? WorkspaceRole.Producer : currentMember?.Role;
            var pendingCount = pendingTasksCount.TryGetValue(w.Id, out var count) ? count : 0;

            return new WorkspaceDto(
                Id: w.Id,
                Name: w.Name,
                OwnerId: w.Owner.Id,
                OwnerName: w.Owner.FullName,
                OwnerEmail: w.Owner.Email,
                StorageQuotaBytes: w.StorageQuotaBytes,
                UsedStorageBytes: w.UsedStorageBytes,
                CreatedAt: w.CreatedAt,
                IsOwner: isOwner,
                CurrentUserRole: role,
                MemberCount: w.Members.Count(m => !m.IsDeleted),
                PendingTasks: pendingCount
            );
        });
    }

    public async Task<WorkspaceDto> GetWorkspaceByIdAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var workspace = await _dbContext.StudioWorkspaces
            .Include(w => w.Owner)
            .Include(w => w.Members.Where(m => !m.IsDeleted))
            .FirstOrDefaultAsync(w => w.Id == workspaceId && !w.IsDeleted, cancellationToken);

        if (workspace is null)
        {
            throw new ArgumentException("Workspace không tồn tại.");
        }

        var isMember = await _authorizationService.IsMemberAsync(userId, workspaceId, cancellationToken);
        if (!isMember)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền truy cập Workspace này.");
        }

        var pendingCount = await _dbContext.Assignments
            .CountAsync(a => !a.IsDeleted && a.AssigneeUserId == userId && a.Status != AssignmentStatus.Approved && a.Series.WorkspaceId == workspaceId, cancellationToken);

        var isOwner = workspace.OwnerId == userId;
        var currentMember = workspace.Members.FirstOrDefault(m => m.UserId == userId);
        var role = isOwner ? WorkspaceRole.Producer : currentMember?.Role;

        return new WorkspaceDto(
            Id: workspace.Id,
            Name: workspace.Name,
            OwnerId: workspace.Owner.Id,
            OwnerName: workspace.Owner.FullName,
            OwnerEmail: workspace.Owner.Email,
            StorageQuotaBytes: workspace.StorageQuotaBytes,
            UsedStorageBytes: workspace.UsedStorageBytes,
            CreatedAt: workspace.CreatedAt,
            IsOwner: isOwner,
            CurrentUserRole: role,
            MemberCount: workspace.Members.Count(m => !m.IsDeleted),
            PendingTasks: pendingCount
        );
    }

    public async Task<WorkspaceDto> UpdateWorkspaceAsync(Guid userId, Guid workspaceId, UpdateWorkspaceRequest request, CancellationToken cancellationToken = default)
    {
        var canManage = await _authorizationService.IsOwnerOrProducerAsync(userId, workspaceId, cancellationToken);
        if (!canManage)
        {
            throw new UnauthorizedAccessException("Chỉ Chủ sở hữu (Owner) hoặc Quản lý sản xuất (Producer) mới có quyền chỉnh sửa Workspace.");
        }

        var workspace = await _dbContext.StudioWorkspaces
            .Include(w => w.Owner)
            .Include(w => w.Members)
            .FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);

        if (workspace is null)
        {
            throw new ArgumentException("Workspace không tồn tại.");
        }

        workspace.Rename(request.Name);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var isOwner = workspace.OwnerId == userId;
        var currentMember = workspace.Members.FirstOrDefault(m => m.UserId == userId);
        var role = isOwner ? WorkspaceRole.Producer : currentMember?.Role;

        return new WorkspaceDto(
            Id: workspace.Id,
            Name: workspace.Name,
            OwnerId: workspace.Owner.Id,
            OwnerName: workspace.Owner.FullName,
            OwnerEmail: workspace.Owner.Email,
            StorageQuotaBytes: workspace.StorageQuotaBytes,
            UsedStorageBytes: workspace.UsedStorageBytes,
            CreatedAt: workspace.CreatedAt,
            IsOwner: isOwner,
            CurrentUserRole: role,
            MemberCount: workspace.Members.Count
        );
    }

    public async Task DeleteWorkspaceAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var isOwner = await _authorizationService.IsOwnerAsync(userId, workspaceId, cancellationToken);
        if (!isOwner)
        {
            throw new UnauthorizedAccessException("Chỉ Chủ sở hữu (Owner) mới có quyền xóa Workspace.");
        }

        var workspace = await _dbContext.StudioWorkspaces
            .Include(w => w.Members)
            .FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);

        if (workspace is null)
        {
            throw new ArgumentException("Workspace không tồn tại.");
        }

        // Xóa thành viên và xóa workspace
        _dbContext.WorkspaceMembers.RemoveRange(workspace.Members);
        _dbContext.StudioWorkspaces.Remove(workspace);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IEnumerable<WorkspaceMemberDto>> GetMembersAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var isMember = await _authorizationService.IsMemberAsync(userId, workspaceId, cancellationToken);
        if (!isMember)
        {
            throw new UnauthorizedAccessException("Bạn phải là thành viên của Workspace để xem danh sách thành viên.");
        }

        var workspace = await _dbContext.StudioWorkspaces
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);

        if (workspace is null)
        {
            throw new ArgumentException("Workspace không tồn tại.");
        }

        var members = await _dbContext.WorkspaceMembers
            .Include(m => m.User)
            .Where(m => m.WorkspaceId == workspaceId)
            .OrderBy(m => m.JoinedAt)
            .ToListAsync(cancellationToken);

        return members.Select(m => new WorkspaceMemberDto(
            UserId: m.UserId,
            FullName: m.User.FullName,
            Email: m.User.Email,
            PhoneNumber: m.User.PhoneNumber,
            AvatarUrl: m.User.AvatarUrl,
            Role: m.Role,
            IsOwner: m.UserId == workspace.OwnerId,
            JoinedAt: m.JoinedAt
        ));
    }

    public async Task<WorkspaceMemberDto> AddMemberAsync(Guid actorId, Guid workspaceId, AddWorkspaceMemberRequest request, CancellationToken cancellationToken = default)
    {
        var canManage = await _authorizationService.IsOwnerOrProducerAsync(actorId, workspaceId, cancellationToken);
        if (!canManage)
        {
            throw new UnauthorizedAccessException("Chỉ Chủ sở hữu (Owner) hoặc Quản lý sản xuất (Producer) mới có quyền thêm thành viên.");
        }

        var workspace = await _dbContext.StudioWorkspaces
            .FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);

        if (workspace is null)
        {
            throw new ArgumentException("Workspace không tồn tại.");
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var targetUser = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        string? inviteOtp = null;
        if (targetUser is null)
        {
            // Tài khoản được mời: KHÔNG có mật khẩu (không đăng nhập được) và chưa xác thực email.
            // Người được mời tự đặt mật khẩu bằng OTP gửi tới email; đặt thành công sẽ xác thực email
            // (AuthService.ResetPasswordAsync).
            inviteOtp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            targetUser = User.Create(
                email: normalizedEmail,
                fullName: normalizedEmail.Split('@')[0],
                passwordHash: null,
                role: SystemRole.User);
            targetUser.SetPasswordResetToken(inviteOtp, DateTime.UtcNow.Add(InviteOtpLifetime));
            _dbContext.Users.Add(targetUser);
        }
        else
        {
            if (!targetUser.IsActive)
            {
                throw new ArgumentException($"Tài khoản người dùng với email '{request.Email}' hiện đang bị vô hiệu hóa.");
            }

            // BR-07: Administrator holds no workspace_members row in any Studio
            if (targetUser.Role == SystemRole.Admin)
            {
                throw new InvalidOperationException("Administrator không được phép tham gia Workspace với tư cách thành viên (BR-07).");
            }
        }

        var isAlreadyMember = inviteOtp is null && await _dbContext.WorkspaceMembers
            .AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == targetUser.Id, cancellationToken);

        if (isAlreadyMember)
        {
            throw new InvalidOperationException("Người dùng này đã là thành viên của Workspace.");
        }

        var newMember = WorkspaceMember.Create(workspaceId, targetUser.Id, request.Role);
        _dbContext.WorkspaceMembers.Add(newMember);

        // Tạo user (nếu là lời mời) và membership trong cùng một lần lưu
        await _dbContext.SaveChangesAsync(cancellationToken);

        await SendMemberInvitationEmailAsync(targetUser, workspace.Name, newMember.Role, inviteOtp, cancellationToken);

        return new WorkspaceMemberDto(
            UserId: targetUser.Id,
            FullName: targetUser.FullName,
            Email: targetUser.Email,
            PhoneNumber: targetUser.PhoneNumber,
            AvatarUrl: targetUser.AvatarUrl,
            Role: newMember.Role,
            IsOwner: targetUser.Id == workspace.OwnerId,
            JoinedAt: newMember.JoinedAt
        );
    }

    /// <summary>
    /// UC-01 / UC-03 bước 4: thông báo cho người được thêm vào Studio.
    /// inviteOtp != null nghĩa là tài khoản vừa được tạo từ lời mời và cần đặt mật khẩu.
    /// Lỗi gửi email không làm hỏng thao tác thêm thành viên (đã lưu).
    /// </summary>
    private async Task SendMemberInvitationEmailAsync(
        User user, string workspaceName, WorkspaceRole role, string? inviteOtp, CancellationToken cancellationToken)
    {
        var safeName = WebUtility.HtmlEncode(user.FullName);
        var safeWorkspace = WebUtility.HtmlEncode(workspaceName);

        string subject;
        string body;
        if (inviteOtp is not null)
        {
            subject = $"[PanelForge] Bạn được mời tham gia Studio {workspaceName}";
            body = $@"
            <h3>Xin chào {safeName},</h3>
            <p>Bạn được mời tham gia Studio <strong>{safeWorkspace}</strong> trên PanelForge với vai trò <strong>{role}</strong>.</p>
            <p>Một tài khoản đã được tạo cho email này. Để kích hoạt, hãy mở trang <em>Đặt lại mật khẩu</em> và nhập mã OTP:</p>
            <p><strong><span style='font-size: 24px; color: #007bff;'>{inviteOtp}</span></strong></p>
            <p>Mã có hiệu lực trong {(int)InviteOtpLifetime.TotalMinutes} phút. Nếu mã hết hạn, hãy dùng chức năng <em>Quên mật khẩu</em> với email này để nhận mã mới.</p>
            <p>Nếu bạn không mong đợi lời mời này, hãy bỏ qua email.</p>
            <br/>
            <p>Trân trọng,<br/>PanelForge Studio Team</p>";
        }
        else
        {
            subject = $"[PanelForge] Bạn đã được thêm vào Studio {workspaceName}";
            body = $@"
            <h3>Xin chào {safeName},</h3>
            <p>Bạn đã được thêm vào Studio <strong>{safeWorkspace}</strong> trên PanelForge với vai trò <strong>{role}</strong>.</p>
            <p>Đăng nhập để xem Studio trong mục <em>Your Workspaces</em>.</p>
            <br/>
            <p>Trân trọng,<br/>PanelForge Studio Team</p>";
        }

        try
        {
            await _emailService.SendEmailAsync(user.Email, subject, body, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không gửi được email mời thành viên tới {Email}", user.Email);
        }
    }

    public async Task<WorkspaceMemberDto> UpdateMemberRoleAsync(Guid actorId, Guid workspaceId, Guid targetUserId, UpdateMemberRoleRequest request, CancellationToken cancellationToken = default)
    {
        var canManage = await _authorizationService.IsOwnerOrProducerAsync(actorId, workspaceId, cancellationToken);
        if (!canManage)
        {
            throw new UnauthorizedAccessException("Chỉ Chủ sở hữu (Owner) hoặc Quản lý sản xuất (Producer) mới có quyền đổi vai trò thành viên.");
        }

        var workspace = await _dbContext.StudioWorkspaces
            .FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);

        if (workspace is null)
        {
            throw new ArgumentException("Workspace không tồn tại.");
        }

        if (targetUserId == workspace.OwnerId)
        {
            throw new InvalidOperationException("Không thể thay đổi vai trò của Chủ sở hữu (Owner) Workspace.");
        }

        var member = await _dbContext.WorkspaceMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == targetUserId, cancellationToken);

        if (member is null)
        {
            throw new ArgumentException("Thành viên không tồn tại trong Workspace này.");
        }

        member.ChangeRole(request.NewRole);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new WorkspaceMemberDto(
            UserId: member.User.Id,
            FullName: member.User.FullName,
            Email: member.User.Email,
            PhoneNumber: member.User.PhoneNumber,
            AvatarUrl: member.User.AvatarUrl,
            Role: member.Role,
            IsOwner: false,
            JoinedAt: member.JoinedAt
        );
    }

    public async Task RemoveMemberAsync(Guid actorId, Guid workspaceId, Guid targetUserId, CancellationToken cancellationToken = default)
    {
        var workspace = await _dbContext.StudioWorkspaces
            .FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);

        if (workspace is null)
        {
            throw new ArgumentException("Workspace không tồn tại.");
        }

        if (targetUserId == workspace.OwnerId)
        {
            throw new InvalidOperationException("Không thể xóa Chủ sở hữu (Owner) khỏi Workspace.");
        }

        // Cho phép: Owner, Producer, hoặc chính thành viên đó tự rời khỏi Workspace
        var isSelfLeaving = actorId == targetUserId;
        var canManage = await _authorizationService.IsOwnerOrProducerAsync(actorId, workspaceId, cancellationToken);

        if (!canManage && !isSelfLeaving)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền xóa thành viên này khỏi Workspace.");
        }

        var member = await _dbContext.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == targetUserId, cancellationToken);

        if (member is null)
        {
            throw new ArgumentException("Thành viên không tồn tại trong Workspace này.");
        }

        _dbContext.WorkspaceMembers.Remove(member);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
