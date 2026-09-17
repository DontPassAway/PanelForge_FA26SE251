using FluentAssertions;
using Moq;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Application.Users.Commands.UpdateUserProfile;
using PanelForge.Domain.Entities.Auth;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Users;

public class UpdateUserProfileCommandHandlerTests
{
    private readonly Mock<IPanelForgeDbContext> _dbContextMock;

    public UpdateUserProfileCommandHandlerTests()
    {
        _dbContextMock = new Mock<IPanelForgeDbContext>();
    }

    [Fact]
    public async Task Handle_UserNotFound_ShouldReturnNull()
    {
        // Arrange
        var users = new List<User>();
        _dbContextMock.Setup(db => db.Users).Returns(DbSetMockHelper.CreateDbSetMock(users));

        var handler = new UpdateUserProfileCommandHandler(_dbContextMock.Object);
        var command = new UpdateUserProfileCommand(
            TargetUserId: Guid.NewGuid(),
            TargetFirebaseUid: string.Empty,
            CurrentUserIdStr: string.Empty,
            CurrentUserFirebaseUid: string.Empty,
            IsAdmin: false,
            Username: "New Name",
            PhoneNumber: "0911223344",
            Avatar: "https://avatar.com/pic.png");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Unauthorized_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        var user = User.Create("user@test.com", "Old Name");
        var users = new List<User> { user };
        _dbContextMock.Setup(db => db.Users).Returns(DbSetMockHelper.CreateDbSetMock(users));

        var handler = new UpdateUserProfileCommandHandler(_dbContextMock.Object);
        var command = new UpdateUserProfileCommand(
            TargetUserId: user.Id,
            TargetFirebaseUid: null,
            CurrentUserIdStr: Guid.NewGuid().ToString(), // different user
            CurrentUserFirebaseUid: "different-uid",
            IsAdmin: false,
            Username: "New Name",
            PhoneNumber: null,
            Avatar: null);

        // Act
        var act = async () => await handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*không có quyền chỉnh sửa*");
    }

    [Fact]
    public async Task Handle_OwnerUpdate_ShouldUpdateProfileAndReturnDto()
    {
        // Arrange
        var user = User.Create("user@test.com", "Old Name");
        var users = new List<User> { user };
        _dbContextMock.Setup(db => db.Users).Returns(DbSetMockHelper.CreateDbSetMock(users));
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new UpdateUserProfileCommandHandler(_dbContextMock.Object);
        var command = new UpdateUserProfileCommand(
            TargetUserId: user.Id,
            TargetFirebaseUid: string.Empty,
            CurrentUserIdStr: user.Id.ToString(),
            CurrentUserFirebaseUid: string.Empty,
            IsAdmin: false,
            Username: "Updated Name",
            PhoneNumber: "0999888777",
            Avatar: "https://img.com/avatar.jpg");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Username.Should().Be("Updated Name");
        result.PhoneNumber.Should().Be("0999888777");
        result.Avatar.Should().Be("https://img.com/avatar.jpg");
        user.FullName.Should().Be("Updated Name");
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
