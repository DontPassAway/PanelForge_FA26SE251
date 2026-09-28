using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PanelForge.Application.Interfaces;
using PanelForge.Application.Interfaces.Authentication;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Auth;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Services;
using PanelForge.UnitTests.Common;
using System.Reflection;
using Xunit;

namespace PanelForge.UnitTests.Infrastructure.Services;

public class WorkspaceServiceTests
{
    private readonly Mock<IPanelForgeDbContext> _dbContextMock;
    private readonly Mock<IWorkspaceAuthorizationService> _authorizationServiceMock;
    private readonly Mock<IEmailService> _emailServiceMock;
    private readonly WorkspaceService _service;

    public WorkspaceServiceTests()
    {
        _dbContextMock = new Mock<IPanelForgeDbContext>();
        _authorizationServiceMock = new Mock<IWorkspaceAuthorizationService>();
        _emailServiceMock = new Mock<IEmailService>();
        _service = new WorkspaceService(
            _dbContextMock.Object,
            _authorizationServiceMock.Object,
            _emailServiceMock.Object,
            NullLogger<WorkspaceService>.Instance);
    }

    [Fact]
    public async Task GetUserWorkspacesAsync_ShouldFilterIsDeletedAndCountPendingTasks()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var owner = User.Create("owner@test.com", "Owner");
        owner.GetType().GetProperty("Id")?.SetValue(owner, userId);

        var workspace = StudioWorkspace.Create("Test Studio", owner.Id);
        
        // Use reflection to set private property if necessary, but owner is set via Create
        workspace.GetType().GetProperty("Owner")?.SetValue(workspace, owner);
        
        var deletedMember = WorkspaceMember.Create(workspace.Id, Guid.NewGuid(), WorkspaceRole.Artist);
        deletedMember.GetType().GetProperty("IsDeleted")?.SetValue(deletedMember, true);
        
        var activeMember = WorkspaceMember.Create(workspace.Id, userId, WorkspaceRole.Writer);
        
        // Inject members list using property setter or backing field
        var membersProp = typeof(StudioWorkspace).GetProperty("Members");
        var membersList = new List<WorkspaceMember> { deletedMember, activeMember };
        membersProp?.SetValue(workspace, membersList);

        var series = Series.Create(workspace.Id, "Series 1", ReadingDirection.LeftToRight);
        series.GetType().GetProperty("WorkspaceId")?.SetValue(series, workspace.Id);

        var task1 = Assignment.Create(series.Id, Guid.NewGuid(), WorkflowEntityType.Page, Guid.NewGuid(), Guid.NewGuid(), userId, WorkspaceRole.Artist, "Task 1");
        task1.GetType().GetProperty("Series")?.SetValue(task1, series);
        // task1 is Assigned (Pending)

        var task2 = Assignment.Create(series.Id, Guid.NewGuid(), WorkflowEntityType.Page, Guid.NewGuid(), Guid.NewGuid(), userId, WorkspaceRole.Artist, "Task 2");
        task2.MarkApproved(); // Approved, should not count
        task2.GetType().GetProperty("Series")?.SetValue(task2, series);

        var task3 = Assignment.Create(series.Id, Guid.NewGuid(), WorkflowEntityType.Page, Guid.NewGuid(), Guid.NewGuid(), userId, WorkspaceRole.Artist, "Task 3");
        task3.GetType().GetProperty("IsDeleted")?.SetValue(task3, true); // Deleted task, should not count
        task3.GetType().GetProperty("Series")?.SetValue(task3, series);

        var workspaces = new List<StudioWorkspace> { workspace };
        var assignments = new List<Assignment> { task1, task2, task3 };

        _dbContextMock.Setup(db => db.StudioWorkspaces).Returns(DbSetMockHelper.CreateDbSetMock(workspaces));
        _dbContextMock.Setup(db => db.Assignments).Returns(DbSetMockHelper.CreateDbSetMock(assignments));

        // Act
        var result = await _service.GetUserWorkspacesAsync(userId);

        // Assert
        result.Should().HaveCount(1);
        var dto = result.First();
        dto.PendingTasks.Should().Be(1); // Only task1 should be counted
        dto.MemberCount.Should().Be(1); // Since MemberCount uses w.Members.Count but we included with `.Where(m => !m.IsDeleted)`, EF in-memory mock might not apply `.Where` to navigation properties accurately without proper setup, but the test focuses on the query logic.
    }

    [Fact]
    public async Task AddMemberAsync_WhenTargetUserIsAdmin_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var actorId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var workspace = StudioWorkspace.Create("Test Studio", actorId);
        typeof(StudioWorkspace).GetProperty("Id")?.SetValue(workspace, workspaceId);

        var adminUser = User.Create("admin@test.com", "Admin User", role: SystemRole.Admin);

        _authorizationServiceMock
            .Setup(a => a.IsOwnerOrProducerAsync(actorId, workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var workspacesDbSet = DbSetMockHelper.CreateDbSetMock(new List<StudioWorkspace> { workspace });
        var usersDbSet = DbSetMockHelper.CreateDbSetMock(new List<User> { adminUser });
        var membersDbSet = DbSetMockHelper.CreateDbSetMock(new List<WorkspaceMember>());

        _dbContextMock.Setup(db => db.StudioWorkspaces).Returns(workspacesDbSet);
        _dbContextMock.Setup(db => db.Users).Returns(usersDbSet);
        _dbContextMock.Setup(db => db.WorkspaceMembers).Returns(membersDbSet);

        var request = new PanelForge.Application.DTOs.Workspaces.AddWorkspaceMemberRequest
        {
            Email = adminUser.Email,
            Role = WorkspaceRole.Artist
        };

        // Act
        var act = async () => await _service.AddMemberAsync(actorId, workspaceId, request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Administrator*");
    }

    [Fact]
    public async Task AddMemberAsync_WhenEmailHasNoAccount_ShouldCreatePasswordlessInvitedUser_AndSendOtp()
    {
        // Arrange
        var actorId = Guid.NewGuid();
        var workspace = StudioWorkspace.Create("Test Studio", actorId);

        _authorizationServiceMock
            .Setup(a => a.IsOwnerOrProducerAsync(actorId, workspace.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var users = new List<User>();
        var usersDbSet = DbSetMockHelper.CreateDbSetMock(users);

        _dbContextMock.Setup(db => db.StudioWorkspaces).Returns(DbSetMockHelper.CreateDbSetMock(new List<StudioWorkspace> { workspace }));
        _dbContextMock.Setup(db => db.Users).Returns(usersDbSet);
        _dbContextMock.Setup(db => db.WorkspaceMembers).Returns(DbSetMockHelper.CreateDbSetMock(new List<WorkspaceMember>()));

        var request = new PanelForge.Application.DTOs.Workspaces.AddWorkspaceMemberRequest
        {
            Email = "New.Artist@Test.com",
            Role = WorkspaceRole.Artist
        };

        // Act
        var result = await _service.AddMemberAsync(actorId, workspace.Id, request);

        // Assert
        var invited = users.Should().ContainSingle().Subject;
        invited.Email.Should().Be("new.artist@test.com");
        invited.PasswordHash.Should().BeNull("tài khoản được mời không được có mật khẩu mặc định");
        invited.IsEmailConfirmed.Should().BeFalse();
        invited.PasswordResetToken.Should().MatchRegex("^[0-9]{6}$");
        invited.PasswordResetTokenExpiresAt.Should().BeAfter(DateTime.UtcNow);
        result.Role.Should().Be(WorkspaceRole.Artist);

        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _emailServiceMock.Verify(e => e.SendEmailAsync(
            "new.artist@test.com",
            It.IsAny<string>(),
            It.Is<string>(body => body.Contains(invited.PasswordResetToken!)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddMemberAsync_WhenEmailSendingFails_ShouldStillAddMember()
    {
        // Arrange
        var actorId = Guid.NewGuid();
        var workspace = StudioWorkspace.Create("Test Studio", actorId);
        var existingUser = User.Create("writer@test.com", "Writer");

        _authorizationServiceMock
            .Setup(a => a.IsOwnerOrProducerAsync(actorId, workspace.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _emailServiceMock
            .Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentNullException("SenderEmail"));

        _dbContextMock.Setup(db => db.StudioWorkspaces).Returns(DbSetMockHelper.CreateDbSetMock(new List<StudioWorkspace> { workspace }));
        _dbContextMock.Setup(db => db.Users).Returns(DbSetMockHelper.CreateDbSetMock(new List<User> { existingUser }));
        _dbContextMock.Setup(db => db.WorkspaceMembers).Returns(DbSetMockHelper.CreateDbSetMock(new List<WorkspaceMember>()));

        var request = new PanelForge.Application.DTOs.Workspaces.AddWorkspaceMemberRequest
        {
            Email = existingUser.Email,
            Role = WorkspaceRole.Writer
        };

        // Act
        var result = await _service.AddMemberAsync(actorId, workspace.Id, request);

        // Assert
        result.UserId.Should().Be(existingUser.Id);
        existingUser.PasswordResetToken.Should().BeNull("user đã có tài khoản không cần OTP kích hoạt");
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
