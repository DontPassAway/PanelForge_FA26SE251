using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Admin.Commands.AssignUserRole;
using PanelForge.Application.Features.Admin.Commands.CreateUserByAdmin;
using PanelForge.Application.Features.Admin.Commands.ToggleUserStatus;
using PanelForge.Application.Features.Admin.Queries.GetAdminUsers;
using PanelForge.Application.Common.Interfaces.Authentication;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Admin;

public class AdminUserCommandTests
{
    [Fact]
    public async Task AssignRole_Producer_ShouldGrantStudioCreationAndKeepUserRole()
    {
        await using var db = InMemoryDb.Create();
        var user = User.Create("a@test.com", "A");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await new AssignUserRoleCommandHandler(db)
            .Handle(new AssignUserRoleCommand(user.Id, "Producer"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.NewRole.Should().Be("User");
        result.Value.CanCreateStudio.Should().BeTrue();
    }

    [Fact]
    public async Task AssignRole_AdminForWorkspaceMember_ShouldBeBlocked_BR07()
    {
        await using var db = InMemoryDb.Create();
        var user = User.Create("m@test.com", "M");
        db.Users.Add(user);
        db.WorkspaceMembers.Add(WorkspaceMember.Create(Guid.NewGuid(), user.Id, WorkspaceRole.Writer));
        await db.SaveChangesAsync();

        var result = await new AssignUserRoleCommandHandler(db)
            .Handle(new AssignUserRoleCommand(user.Id, "Admin"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().BeNull();
        user.Role.Should().Be(SystemRole.User);
    }

    [Fact]
    public async Task AssignRole_UnknownUser_ShouldReturnNotFoundCode()
    {
        await using var db = InMemoryDb.Create();

        var result = await new AssignUserRoleCommandHandler(db)
            .Handle(new AssignUserRoleCommand(Guid.NewGuid(), "Admin"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task CreateUser_DuplicateEmail_ShouldFail()
    {
        await using var db = InMemoryDb.Create();
        db.Users.Add(User.Create("dup@test.com", "Dup"));
        await db.SaveChangesAsync();
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.HashPassword(It.IsAny<string>())).Returns("hash");

        var result = await new CreateUserByAdminCommandHandler(db, hasher.Object).Handle(
            new CreateUserByAdminCommand(" DUP@test.com ", "Other", "secret1", null, SystemRole.User, true),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task CreateUser_Active_ShouldConfirmEmailAndNormalize()
    {
        await using var db = InMemoryDb.Create();
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.HashPassword("secret1")).Returns("hash");

        var result = await new CreateUserByAdminCommandHandler(db, hasher.Object).Handle(
            new CreateUserByAdminCommand(" New@Test.com ", "New", "secret1", null, SystemRole.User, true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Email.Should().Be("new@test.com");
        result.Value.IsActive.Should().BeTrue();
        result.Value.IsEmailConfirmed.Should().BeTrue();
    }

    [Fact]
    public async Task ToggleStatus_ShouldFlipIsActive()
    {
        await using var db = InMemoryDb.Create();
        var user = User.Create("t@test.com", "T");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var wasActive = user.IsActive;

        var result = await new ToggleUserStatusCommandHandler(db)
            .Handle(new ToggleUserStatusCommand(user.Id), CancellationToken.None);

        result.Value!.IsActive.Should().Be(!wasActive);
    }

    [Fact]
    public async Task GetUsers_ShouldClampPageSizeAndReturnTotal()
    {
        await using var db = InMemoryDb.Create();
        db.Users.AddRange(User.Create("1@test.com", "1"), User.Create("2@test.com", "2"));
        await db.SaveChangesAsync();

        var result = await new GetAdminUsersQueryHandler(db)
            .Handle(new GetAdminUsersQuery(Page: 0, PageSize: 500), CancellationToken.None);

        result.Value!.Total.Should().Be(2);
        result.Value.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(20);
        result.Value.Items.Should().HaveCount(2);
    }
}
