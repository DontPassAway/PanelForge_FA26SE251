using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Admin.Commands.AssignUserRole;
using PanelForge.Application.Features.Admin.Commands.CreateUserByAdmin;
using PanelForge.Application.Features.Admin.Commands.DeactivateUser;
using PanelForge.Application.Features.Admin.Commands.UpdateUserByAdmin;
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

    // ─── Q12 / AUD-BE-08: chỉ một tài khoản Publisher đang hoạt động ─────────

    [Fact]
    public async Task SingleActivePublisher_SecondActiveCreateAssignUpdateOrReactivate_ReturnsConflict()
    {
        await using var db = InMemoryDb.Create();
        var pub1 = User.Create("pub1@test.com", "Publisher 1", role: SystemRole.Publisher);
        var other = User.Create("other@test.com", "Other User", role: SystemRole.User);
        var inactivePub = User.Create("oldpub@test.com", "Old Publisher", role: SystemRole.Publisher);
        inactivePub.Deactivate();
        db.Users.AddRange(pub1, other, inactivePub);
        await db.SaveChangesAsync();

        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.HashPassword(It.IsAny<string>())).Returns("hash");

        // 1. Create second active Publisher -> 409 Conflict
        var createRes = await new CreateUserByAdminCommandHandler(db, hasher.Object).Handle(
            new CreateUserByAdminCommand("pub2@test.com", "Pub 2", "secret1", null, SystemRole.Publisher, true),
            CancellationToken.None);
        createRes.IsSuccess.Should().BeFalse();
        createRes.ErrorCode.Should().Be(ResultErrorCodes.Conflict);

        // 2. Assign Publisher role to another active user -> 409 Conflict
        var assignRes = await new AssignUserRoleCommandHandler(db)
            .Handle(new AssignUserRoleCommand(other.Id, "Publisher"), CancellationToken.None);
        assignRes.IsSuccess.Should().BeFalse();
        assignRes.ErrorCode.Should().Be(ResultErrorCodes.Conflict);

        // 3. Update another user to active Publisher -> 409 Conflict
        var updateRes = await new PanelForge.Application.Features.Admin.Commands.UpdateUserByAdmin.UpdateUserByAdminCommandHandler(db)
            .Handle(new(other.Id, "Other User", null, SystemRole.Publisher, true), CancellationToken.None);
        updateRes.IsSuccess.Should().BeFalse();
        updateRes.ErrorCode.Should().Be(ResultErrorCodes.Conflict);

        // 4. Re-activate an inactive Publisher while another is active -> 409 Conflict
        var toggleRes = await new ToggleUserStatusCommandHandler(db)
            .Handle(new ToggleUserStatusCommand(inactivePub.Id), CancellationToken.None);
        toggleRes.IsSuccess.Should().BeFalse();
        toggleRes.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }

    [Fact]
    public async Task SingleActivePublisher_AfterDeactivatingOldPublisher_CanAssignNewPublisher()
    {
        await using var db = InMemoryDb.Create();
        var pub1 = User.Create("pub1@test.com", "Publisher 1", role: SystemRole.Publisher);
        var next = User.Create("pub2@test.com", "Publisher 2", role: SystemRole.User);
        db.Users.AddRange(pub1, next);
        await db.SaveChangesAsync();

        // Deactivate old Publisher
        var deactivated = await new ToggleUserStatusCommandHandler(db)
            .Handle(new ToggleUserStatusCommand(pub1.Id), CancellationToken.None);
        deactivated.IsSuccess.Should().BeTrue();
        deactivated.Value!.IsActive.Should().BeFalse();

        // Now assigning new active Publisher succeeds
        var assigned = await new AssignUserRoleCommandHandler(db)
            .Handle(new AssignUserRoleCommand(next.Id, "Publisher"), CancellationToken.None);
        assigned.IsSuccess.Should().BeTrue();
        assigned.Value!.NewRole.Should().Be("Publisher");
    }

    [Fact]
    public void UserModel_HasUniqueFilteredIndexForSingleActivePublisher()
    {
        using var db = InMemoryDb.Create();
        var entityType = db.Model.FindEntityType(typeof(User))!;
        var index = entityType.GetIndexes().FirstOrDefault(i =>
            i.IsUnique && i.Properties.Any(p => p.Name == nameof(User.Role)));
        index.Should().NotBeNull("Q12 requires a unique partial index on users(role) for active Publisher");
    }

    [Fact]
    public async Task LastActiveAdmin_CannotBeDemotedLockedOrDeactivated()
    {
        await using var db = InMemoryDb.Create();
        var admin = User.Create("root@test.com", "Root", role: SystemRole.Admin);
        admin.Activate();
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var assign = await new AssignUserRoleCommandHandler(db)
            .Handle(new AssignUserRoleCommand(admin.Id, "User"), CancellationToken.None);
        var update = await new UpdateUserByAdminCommandHandler(db)
            .Handle(new UpdateUserByAdminCommand(admin.Id, "Root", null, SystemRole.Admin, false), CancellationToken.None);
        var toggle = await new ToggleUserStatusCommandHandler(db)
            .Handle(new ToggleUserStatusCommand(admin.Id), CancellationToken.None);
        var deactivate = await new DeactivateUserCommandHandler(db)
            .Handle(new DeactivateUserCommand(admin.Id), CancellationToken.None);

        new[] { assign.ErrorCode, update.ErrorCode, toggle.ErrorCode, deactivate.ErrorCode }
            .Should().AllBe(ResultErrorCodes.Conflict);
        admin.Role.Should().Be(SystemRole.Admin);
        admin.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task AdminCreatedInApp_AllowsDemotingTheOriginalAdmin()
    {
        await using var db = InMemoryDb.Create();
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.HashPassword(It.IsAny<string>())).Returns("hash");
        var root = User.Create("root@test.com", "Root", role: SystemRole.Admin);
        root.Activate();
        db.Users.Add(root);
        await db.SaveChangesAsync();

        var created = await new CreateUserByAdminCommandHandler(db, hasher.Object).Handle(
            new CreateUserByAdminCommand("second@test.com", "Second", "Secret#123", null, SystemRole.Admin, true),
            CancellationToken.None);
        var demote = await new AssignUserRoleCommandHandler(db)
            .Handle(new AssignUserRoleCommand(root.Id, "User"), CancellationToken.None);

        created.IsSuccess.Should().BeTrue();
        created.Value!.Role.Should().Be("Admin");
        demote.IsSuccess.Should().BeTrue();
        root.Role.Should().Be(SystemRole.User);
    }
}
