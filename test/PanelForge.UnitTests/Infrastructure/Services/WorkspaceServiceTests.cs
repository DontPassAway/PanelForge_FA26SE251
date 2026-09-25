using FluentAssertions;
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
    private readonly Mock<IPasswordHasher> _passwordHasherMock;
    private readonly WorkspaceService _service;

    public WorkspaceServiceTests()
    {
        _dbContextMock = new Mock<IPanelForgeDbContext>();
        _authorizationServiceMock = new Mock<IWorkspaceAuthorizationService>();
        _passwordHasherMock = new Mock<IPasswordHasher>();
        _service = new WorkspaceService(_dbContextMock.Object, _authorizationServiceMock.Object, _passwordHasherMock.Object);
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
}
