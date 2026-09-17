using FluentAssertions;
using PanelForge.Domain.Entities.Auth;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.Domain;

public class StudioWorkspaceTests
{
    [Fact]
    public void Create_ValidParameters_ShouldCreateStudioWorkspace()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var name = "  Manga Studio Alpha  ";
        long quota = 1024 * 1024 * 100; // 100MB

        // Act
        var workspace = StudioWorkspace.Create(name, ownerId, quota);

        // Assert
        workspace.Name.Should().Be("Manga Studio Alpha");
        workspace.OwnerId.Should().Be(ownerId);
        workspace.StorageQuotaBytes.Should().Be(quota);
        workspace.UsedStorageBytes.Should().Be(0);
        workspace.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyName_ShouldThrowArgumentException(string invalidName)
    {
        var act = () => StudioWorkspace.Create(invalidName, Guid.NewGuid());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_NegativeStorageQuota_ShouldThrowArgumentOutOfRangeException()
    {
        var act = () => StudioWorkspace.Create("Valid Name", Guid.NewGuid(), -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Rename_ValidName_ShouldUpdateName()
    {
        var workspace = StudioWorkspace.Create("Old Name", Guid.NewGuid());
        workspace.Rename("  New Studio Name  ");
        workspace.Name.Should().Be("New Studio Name");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_InvalidName_ShouldThrowArgumentException(string invalidName)
    {
        var workspace = StudioWorkspace.Create("Valid Name", Guid.NewGuid());
        var act = () => workspace.Rename(invalidName);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateUsedStorage_ValidBytes_ShouldUpdateUsedStorage()
    {
        var workspace = StudioWorkspace.Create("Studio", Guid.NewGuid());
        workspace.UpdateUsedStorage(5000);
        workspace.UsedStorageBytes.Should().Be(5000);
    }

    [Fact]
    public void UpdateUsedStorage_NegativeBytes_ShouldThrowArgumentOutOfRangeException()
    {
        var workspace = StudioWorkspace.Create("Studio", Guid.NewGuid());
        var act = () => workspace.UpdateUsedStorage(-10);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void HasStorageCapacity_ZeroQuota_ShouldAlwaysReturnTrue()
    {
        var workspace = StudioWorkspace.Create("Studio", Guid.NewGuid(), storageQuotaBytes: 0);
        workspace.HasStorageCapacity(999999999).Should().BeTrue();
    }

    [Fact]
    public void HasStorageCapacity_WithinQuota_ShouldReturnTrue()
    {
        var workspace = StudioWorkspace.Create("Studio", Guid.NewGuid(), storageQuotaBytes: 1000);
        workspace.UpdateUsedStorage(600);
        workspace.HasStorageCapacity(400).Should().BeTrue();
    }

    [Fact]
    public void HasStorageCapacity_ExceedingQuota_ShouldReturnFalse()
    {
        var workspace = StudioWorkspace.Create("Studio", Guid.NewGuid(), storageQuotaBytes: 1000);
        workspace.UpdateUsedStorage(600);
        workspace.HasStorageCapacity(401).Should().BeFalse();
    }

    [Fact]
    public void WorkspaceMember_CreateAndChangeRole_ShouldWorkCorrectly()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Act
        var member = WorkspaceMember.Create(workspaceId, userId, WorkspaceRole.Writer);

        // Assert
        member.WorkspaceId.Should().Be(workspaceId);
        member.UserId.Should().Be(userId);
        member.Role.Should().Be(WorkspaceRole.Writer);
        member.JoinedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        // Act ChangeRole
        member.ChangeRole(WorkspaceRole.Editor);
        member.Role.Should().Be(WorkspaceRole.Editor);
    }

    [Fact]
    public void User_AssignSystemRole_ShouldUpdateRole()
    {
        // Arrange
        var user = User.Create("admin@test.com", "Admin User");
        user.Role.Should().Be(SystemRole.User);

        // Act
        user.AssignSystemRole(SystemRole.Admin);

        // Assert
        user.Role.Should().Be(SystemRole.Admin);

        user.AssignSystemRole(SystemRole.Moderator);
        user.Role.Should().Be(SystemRole.Moderator);
    }
}
