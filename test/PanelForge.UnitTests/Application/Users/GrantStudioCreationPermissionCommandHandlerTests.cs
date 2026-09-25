using FluentAssertions;
using Moq;
using PanelForge.Application.Features.Users.Commands.GrantStudioPermission;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Auth;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Users;

public class GrantStudioCreationPermissionCommandHandlerTests
{
    private readonly Mock<IPanelForgeDbContext> _dbContextMock;

    public GrantStudioCreationPermissionCommandHandlerTests()
    {
        _dbContextMock = new Mock<IPanelForgeDbContext>();
    }

    [Fact]
    public async Task Handle_WhenUserIsAdmin_ShouldReturnFailure()
    {
        // Arrange (BR-07, BR-22: Admin cannot be granted CanCreateStudio)
        var adminUser = User.Create("admin@test.com", "Admin User", role: SystemRole.Admin);
        var usersDbSet = DbSetMockHelper.CreateDbSetMock(new List<User> { adminUser });
        _dbContextMock.Setup(db => db.Users).Returns(usersDbSet);

        var handler = new GrantStudioCreationPermissionCommandHandler(_dbContextMock.Object);
        var command = new GrantStudioCreationPermissionCommand(adminUser.Id);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Administrator");
        adminUser.CanCreateStudio.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenUserIsValid_ShouldGrantPermission()
    {
        // Arrange
        var normalUser = User.Create("user@test.com", "Normal User", role: SystemRole.User);
        var usersDbSet = DbSetMockHelper.CreateDbSetMock(new List<User> { normalUser });
        _dbContextMock.Setup(db => db.Users).Returns(usersDbSet);
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new GrantStudioCreationPermissionCommandHandler(_dbContextMock.Object);
        var command = new GrantStudioCreationPermissionCommand(normalUser.Id);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.CanCreateStudio.Should().BeTrue();
        normalUser.CanCreateStudio.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_ShouldReturnFailure()
    {
        // Arrange
        var usersDbSet = DbSetMockHelper.CreateDbSetMock(new List<User>());
        _dbContextMock.Setup(db => db.Users).Returns(usersDbSet);

        var handler = new GrantStudioCreationPermissionCommandHandler(_dbContextMock.Object);
        var command = new GrantStudioCreationPermissionCommand(Guid.NewGuid());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("không tồn tại");
    }

    [Fact]
    public async Task Handle_WhenUserIsInactive_ShouldReturnFailure()
    {
        // Arrange
        var inactiveUser = User.Create("inactive@test.com", "Inactive User");
        inactiveUser.Deactivate();
        var usersDbSet = DbSetMockHelper.CreateDbSetMock(new List<User> { inactiveUser });
        _dbContextMock.Setup(db => db.Users).Returns(usersDbSet);

        var handler = new GrantStudioCreationPermissionCommandHandler(_dbContextMock.Object);
        var command = new GrantStudioCreationPermissionCommand(inactiveUser.Id);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("vô hiệu hóa");
    }
}
