using FluentAssertions;
using Moq;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Auth;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Services;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Infrastructure.Services;

public class WorkspaceAuthorizationServiceTests
{
    private readonly Mock<IPanelForgeDbContext> _dbContextMock;
    private readonly WorkspaceAuthorizationService _service;

    public WorkspaceAuthorizationServiceTests()
    {
        _dbContextMock = new Mock<IPanelForgeDbContext>();
        _service = new WorkspaceAuthorizationService(_dbContextMock.Object);
    }

    [Fact]
    public async Task IsOwnerAsync_AdminUser_ShouldReturnTrue()
    {
        var admin = User.Create("admin@test.com", "Admin");
        admin.AssignSystemRole(SystemRole.Admin);

        var users = new List<User> { admin };
        _dbContextMock.Setup(db => db.Users).Returns(DbSetMockHelper.CreateDbSetMock(users));

        var result = await _service.IsOwnerAsync(admin.Id, Guid.NewGuid());
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsOwnerAsync_OwnerUser_ShouldReturnTrue()
    {
        var owner = User.Create("owner@test.com", "Owner");
        var workspace = StudioWorkspace.Create("Studio", owner.Id);

        var users = new List<User> { owner };
        var workspaces = new List<StudioWorkspace> { workspace };

        _dbContextMock.Setup(db => db.Users).Returns(DbSetMockHelper.CreateDbSetMock(users));
        _dbContextMock.Setup(db => db.StudioWorkspaces).Returns(DbSetMockHelper.CreateDbSetMock(workspaces));

        var result = await _service.IsOwnerAsync(owner.Id, workspace.Id);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsOwnerAsync_NonOwnerUser_ShouldReturnFalse()
    {
        var user = User.Create("member@test.com", "Member");
        var workspace = StudioWorkspace.Create("Studio", Guid.NewGuid()); // different owner

        var users = new List<User> { user };
        var workspaces = new List<StudioWorkspace> { workspace };

        _dbContextMock.Setup(db => db.Users).Returns(DbSetMockHelper.CreateDbSetMock(users));
        _dbContextMock.Setup(db => db.StudioWorkspaces).Returns(DbSetMockHelper.CreateDbSetMock(workspaces));

        var result = await _service.IsOwnerAsync(user.Id, workspace.Id);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasWorkspaceRoleAsync_MatchingRole_ShouldReturnTrue()
    {
        var user = User.Create("writer@test.com", "Writer");
        var workspace = StudioWorkspace.Create("Studio", Guid.NewGuid());
        var member = WorkspaceMember.Create(workspace.Id, user.Id, WorkspaceRole.Writer);

        var users = new List<User> { user };
        var workspaces = new List<StudioWorkspace> { workspace };
        var members = new List<WorkspaceMember> { member };

        _dbContextMock.Setup(db => db.Users).Returns(DbSetMockHelper.CreateDbSetMock(users));
        _dbContextMock.Setup(db => db.StudioWorkspaces).Returns(DbSetMockHelper.CreateDbSetMock(workspaces));
        _dbContextMock.Setup(db => db.WorkspaceMembers).Returns(DbSetMockHelper.CreateDbSetMock(members));

        var result = await _service.HasWorkspaceRoleAsync(
            user.Id,
            workspace.Id,
            new[] { WorkspaceRole.Producer, WorkspaceRole.Writer });

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasWorkspaceRoleAsync_NotMatchingRole_ShouldReturnFalse()
    {
        var user = User.Create("editor@test.com", "Editor");
        var workspace = StudioWorkspace.Create("Studio", Guid.NewGuid());
        var member = WorkspaceMember.Create(workspace.Id, user.Id, WorkspaceRole.Editor);

        var users = new List<User> { user };
        var workspaces = new List<StudioWorkspace> { workspace };
        var members = new List<WorkspaceMember> { member };

        _dbContextMock.Setup(db => db.Users).Returns(DbSetMockHelper.CreateDbSetMock(users));
        _dbContextMock.Setup(db => db.StudioWorkspaces).Returns(DbSetMockHelper.CreateDbSetMock(workspaces));
        _dbContextMock.Setup(db => db.WorkspaceMembers).Returns(DbSetMockHelper.CreateDbSetMock(members));

        var result = await _service.HasWorkspaceRoleAsync(
            user.Id,
            workspace.Id,
            new[] { WorkspaceRole.Producer, WorkspaceRole.Writer });

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetMemberRoleAsync_Owner_ShouldReturnProducerRole()
    {
        var ownerId = Guid.NewGuid();
        var workspace = StudioWorkspace.Create("Studio", ownerId);

        _dbContextMock.Setup(db => db.StudioWorkspaces)
            .Returns(DbSetMockHelper.CreateDbSetMock(new List<StudioWorkspace> { workspace }));

        var role = await _service.GetMemberRoleAsync(ownerId, workspace.Id);
        role.Should().Be(WorkspaceRole.Producer);
    }
}
