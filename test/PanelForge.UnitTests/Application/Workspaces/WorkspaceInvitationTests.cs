using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Workspaces.Commands.AddMember;
using PanelForge.Application.Features.Workspaces.Commands.ResendInvitation;
using PanelForge.Application.Features.Workspaces.Commands.UpdateMemberRole;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Workspaces;

/// <summary>Mời thành viên vào Workspace (Producer): validate vai trò, lời mời chờ kích hoạt, gửi lại lời mời, xung đột đồng thời.</summary>
public class WorkspaceInvitationTests
{
    private readonly Mock<IPanelForgeDbContext> _dbContextMock = new();
    private readonly Mock<IWorkspaceAuthorizationService> _authorizationServiceMock = new();
    private readonly Mock<IEmailService> _emailServiceMock = new();

    private AddWorkspaceMemberCommandHandler AddMember() => new(
        _dbContextMock.Object, _authorizationServiceMock.Object, _emailServiceMock.Object,
        NullLogger<AddWorkspaceMemberCommandHandler>.Instance);

    private ResendWorkspaceInvitationCommandHandler ResendInvitation() => new(
        _dbContextMock.Object, _authorizationServiceMock.Object, _emailServiceMock.Object,
        NullLogger<ResendWorkspaceInvitationCommandHandler>.Instance);

    private (StudioWorkspace Workspace, Guid ActorId) Arrange(List<User> users, Func<StudioWorkspace, List<WorkspaceMember>>? members = null)
    {
        var actorId = Guid.NewGuid();
        var workspace = StudioWorkspace.Create("Test Studio", actorId);
        _authorizationServiceMock
            .Setup(a => a.IsOwnerOrProducerAsync(actorId, workspace.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _dbContextMock.Setup(db => db.StudioWorkspaces).Returns(DbSetMockHelper.CreateDbSetMock(new List<StudioWorkspace> { workspace }));
        _dbContextMock.Setup(db => db.Users).Returns(DbSetMockHelper.CreateDbSetMock(users));
        _dbContextMock.Setup(db => db.WorkspaceMembers)
            .Returns(DbSetMockHelper.CreateDbSetMock(members?.Invoke(workspace) ?? new List<WorkspaceMember>()));
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return (workspace, actorId);
    }

    private void EmailFails()
        => _emailServiceMock
            .Setup(e => e.SendWorkspaceInvitationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));

    private void VerifyNoInvitationEmail()
        => _emailServiceMock.Verify(e => e.SendWorkspaceInvitationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);

    private static WorkspaceMember MemberOf(StudioWorkspace workspace, User user, WorkspaceRole role = WorkspaceRole.Artist)
    {
        var member = WorkspaceMember.Create(workspace.Id, user.Id, role);
        typeof(WorkspaceMember).GetProperty(nameof(WorkspaceMember.User))!.SetValue(member, user);
        return member;
    }

    // ── AddMember ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddMember_WhenRoleIsNotDefined_ShouldFailWithoutSaving()
    {
        var (workspace, actorId) = Arrange(new List<User>());

        var result = await AddMember().Handle(
            new AddWorkspaceMemberCommand(actorId, workspace.Id, "x@test.com", (WorkspaceRole)99), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Vai trò không hợp lệ");
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddMember_WhenAlreadyMember_ShouldFail()
    {
        var user = User.Create("dup@test.com", "Dup", "hash");
        var (workspace, actorId) = Arrange(new List<User> { user }, ws => new List<WorkspaceMember> { MemberOf(ws, user) });

        var result = await AddMember().Handle(
            new AddWorkspaceMemberCommand(actorId, workspace.Id, user.Email, WorkspaceRole.Writer), CancellationToken.None);

        result.ErrorMessage.Should().Contain("đã là thành viên");
    }

    [Fact]
    public async Task AddMember_WhenTargetUserDeactivated_ShouldFail()
    {
        var user = User.Create("off@test.com", "Off", "hash");
        user.Deactivate();
        var (workspace, actorId) = Arrange(new List<User> { user });

        var result = await AddMember().Handle(
            new AddWorkspaceMemberCommand(actorId, workspace.Id, user.Email, WorkspaceRole.Writer), CancellationToken.None);

        result.ErrorMessage.Should().Contain("vô hiệu hóa");
        VerifyNoInvitationEmail();
    }

    [Fact]
    public async Task AddMember_WhenProducerInvitesExistingActiveUser_ShouldSucceedWithoutOtp()
    {
        var user = User.Create("writer@test.com", "Writer", "hash");
        user.ConfirmEmail();
        var (workspace, actorId) = Arrange(new List<User> { user });

        var result = await AddMember().Handle(
            new AddWorkspaceMemberCommand(actorId, workspace.Id, user.Email, WorkspaceRole.Producer), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Role.Should().Be(WorkspaceRole.Producer);
        result.Value.InvitationEmailSent.Should().BeTrue();
        result.Value.IsEmailConfirmed.Should().BeTrue();
        user.PasswordResetToken.Should().BeNull();
    }

    [Fact]
    public async Task AddMember_WhenEmailSendingFails_ShouldReportInvitationEmailSentFalse()
    {
        var (workspace, actorId) = Arrange(new List<User>());
        EmailFails();

        var result = await AddMember().Handle(
            new AddWorkspaceMemberCommand(actorId, workspace.Id, "new@test.com", WorkspaceRole.Artist), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.InvitationEmailSent.Should().BeFalse();
        result.Value.IsEmailConfirmed.Should().BeFalse();
    }

    [Fact]
    public async Task AddMember_WhenExistingUserStillPendingInvitation_ShouldIssueFreshOtp()
    {
        var pending = User.Create("pending@test.com", "pending", passwordHash: null);
        pending.SetPasswordResetToken("111111", DateTime.UtcNow.AddMinutes(-30)); // OTP cũ đã hết hạn
        var (workspace, actorId) = Arrange(new List<User> { pending });

        await AddMember().Handle(
            new AddWorkspaceMemberCommand(actorId, workspace.Id, pending.Email, WorkspaceRole.Letterer), CancellationToken.None);

        pending.PasswordResetTokenExpiresAt.Should().BeAfter(DateTime.UtcNow);
        _emailServiceMock.Verify(e => e.SendWorkspaceInvitationAsync(
            pending.Email, It.IsAny<string>(), "Test Studio", "Letterer",
            pending.PasswordResetToken, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddMember_WhenConcurrentDuplicateViolatesUniqueIndex_ShouldFailGracefully()
    {
        var (workspace, actorId) = Arrange(new List<User>());
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("duplicate key"));

        var result = await AddMember().Handle(
            new AddWorkspaceMemberCommand(actorId, workspace.Id, "race@test.com", WorkspaceRole.Artist), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("vừa được thêm");
        VerifyNoInvitationEmail();
    }

    // ── UpdateMemberRole ─────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateMemberRole_WhenRoleIsNotDefined_ShouldFail()
    {
        var (workspace, actorId) = Arrange(new List<User>());

        var result = await new UpdateMemberRoleCommandHandler(_dbContextMock.Object, _authorizationServiceMock.Object)
            .Handle(new UpdateMemberRoleCommand(actorId, workspace.Id, Guid.NewGuid(), (WorkspaceRole)99), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Vai trò không hợp lệ");
    }

    // ── ResendInvitation ─────────────────────────────────────────────────────

    [Fact]
    public async Task ResendInvitation_WhenActorNotProducer_ShouldReturnForbidden()
    {
        var result = await ResendInvitation().Handle(
            new ResendWorkspaceInvitationCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
    }

    [Fact]
    public async Task ResendInvitation_WhenPendingMember_ShouldIssueNewOtpAndSendEmail()
    {
        var pending = User.Create("pending@test.com", "pending", passwordHash: null);
        pending.SetPasswordResetToken("111111", DateTime.UtcNow.AddMinutes(-30));
        var (workspace, actorId) = Arrange(new List<User> { pending }, ws => new List<WorkspaceMember> { MemberOf(ws, pending) });

        var result = await ResendInvitation().Handle(
            new ResendWorkspaceInvitationCommand(actorId, workspace.Id, pending.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.InvitationEmailSent.Should().BeTrue();
        pending.PasswordResetToken.Should().NotBe("111111");
        pending.PasswordResetTokenExpiresAt.Should().BeAfter(DateTime.UtcNow);
        _emailServiceMock.Verify(e => e.SendWorkspaceInvitationAsync(
            pending.Email, It.IsAny<string>(), "Test Studio", "Artist",
            pending.PasswordResetToken, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResendInvitation_WhenAccountAlreadyActivated_ShouldFailWithoutEmail()
    {
        var active = User.Create("active@test.com", "Active", "hash");
        active.ConfirmEmail();
        var (workspace, actorId) = Arrange(new List<User> { active }, ws => new List<WorkspaceMember> { MemberOf(ws, active) });

        var result = await ResendInvitation().Handle(
            new ResendWorkspaceInvitationCommand(actorId, workspace.Id, active.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("đã kích hoạt");
        VerifyNoInvitationEmail();
    }

    [Fact]
    public async Task ResendInvitation_WhenEmailFails_ShouldReportFailure()
    {
        var pending = User.Create("pending@test.com", "pending", passwordHash: null);
        var (workspace, actorId) = Arrange(new List<User> { pending }, ws => new List<WorkspaceMember> { MemberOf(ws, pending) });
        EmailFails();

        var result = await ResendInvitation().Handle(
            new ResendWorkspaceInvitationCommand(actorId, workspace.Id, pending.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Không gửi được email");
    }

    [Fact]
    public async Task ResendInvitation_WhenUserNotMember_ShouldReturnNotFound()
    {
        var (workspace, actorId) = Arrange(new List<User>());

        var result = await ResendInvitation().Handle(
            new ResendWorkspaceInvitationCommand(actorId, workspace.Id, Guid.NewGuid()), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }
}
