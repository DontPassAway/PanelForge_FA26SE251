using FluentAssertions;
using Moq;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Application.Users.Commands.SoftDeleteUser;
using PanelForge.Domain.Entities.Auth;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Users;

public class SoftDeleteUserCommandHandlerTests
{
    private readonly Mock<IPanelForgeDbContext> _dbContextMock;

    public SoftDeleteUserCommandHandlerTests()
    {
        _dbContextMock = new Mock<IPanelForgeDbContext>();
    }

    [Fact]
    public async Task Handle_UserNotFound_ShouldReturnNull()
    {
        // Arrange
        var users = new List<User>();
        _dbContextMock.Setup(db => db.Users).Returns(DbSetMockHelper.CreateDbSetMock(users));

        var handler = new SoftDeleteUserCommandHandler(_dbContextMock.Object);
        var command = new SoftDeleteUserCommand(
            TargetUserId: Guid.NewGuid(),
            TargetFirebaseUid: string.Empty,
            CurrentUserIdStr: string.Empty,
            CurrentUserFirebaseUid: string.Empty,
            IsAdmin: false);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NotAuthorized_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        var targetUser = User.Create("target@example.com", "Target User");
        var otherUserGuid = Guid.NewGuid();
        var users = new List<User> { targetUser };
        _dbContextMock.Setup(db => db.Users).Returns(DbSetMockHelper.CreateDbSetMock(users));

        var handler = new SoftDeleteUserCommandHandler(_dbContextMock.Object);
        var command = new SoftDeleteUserCommand(
            TargetUserId: targetUser.Id,
            TargetFirebaseUid: null,
            CurrentUserIdStr: otherUserGuid.ToString(),
            CurrentUserFirebaseUid: "different-firebase-uid",
            IsAdmin: false);

        // Act
        var act = async () => await handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*không có quyền*");
    }

    [Fact]
    public async Task Handle_AuthorizedByOwnerId_ShouldDeactivateAndReturnTrue()
    {
        // Arrange
        var user = User.Create("user@example.com", "User Name");
        user.IsActive.Should().BeTrue();
        var users = new List<User> { user };
        _dbContextMock.Setup(db => db.Users).Returns(DbSetMockHelper.CreateDbSetMock(users));
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new SoftDeleteUserCommandHandler(_dbContextMock.Object);
        var command = new SoftDeleteUserCommand(
            TargetUserId: user.Id,
            TargetFirebaseUid: string.Empty,
            CurrentUserIdStr: user.Id.ToString(),
            CurrentUserFirebaseUid: string.Empty,
            IsAdmin: false);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        user.IsActive.Should().BeFalse();
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AuthorizedByAdmin_ShouldDeactivateAndReturnTrue()
    {
        // Arrange
        var user = User.Create("user@example.com", "User Name");
        var users = new List<User> { user };
        _dbContextMock.Setup(db => db.Users).Returns(DbSetMockHelper.CreateDbSetMock(users));
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new SoftDeleteUserCommandHandler(_dbContextMock.Object);
        var command = new SoftDeleteUserCommand(
            TargetUserId: user.Id,
            TargetFirebaseUid: null,
            CurrentUserIdStr: Guid.NewGuid().ToString(),
            CurrentUserFirebaseUid: "admin-uid",
            IsAdmin: true);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        user.IsActive.Should().BeFalse();
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
