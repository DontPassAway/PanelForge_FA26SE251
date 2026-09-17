using FluentAssertions;
using Moq;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Application.Users.Queries.GetUserProfile;
using PanelForge.Domain.Entities.Auth;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Users;

public class GetUserProfileQueryHandlerTests
{
    private readonly Mock<IPanelForgeDbContext> _dbContextMock;

    public GetUserProfileQueryHandlerTests()
    {
        _dbContextMock = new Mock<IPanelForgeDbContext>();
    }

    [Fact]
    public async Task Handle_NoIdAndNoFirebaseUid_ShouldThrowArgumentException()
    {
        var handler = new GetUserProfileQueryHandler(_dbContextMock.Object);
        var query = new GetUserProfileQuery(Id: null, FirebaseUid: "");

        var act = async () => await handler.Handle(query, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Phải cung cấp ít nhất*");
    }

    [Fact]
    public async Task Handle_UserNotFound_ShouldReturnNull()
    {
        var users = new List<User>();
        _dbContextMock.Setup(db => db.Users).Returns(DbSetMockHelper.CreateDbSetMock(users));

        var handler = new GetUserProfileQueryHandler(_dbContextMock.Object);
        var query = new GetUserProfileQuery(Id: Guid.NewGuid(), FirebaseUid: null);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_FoundById_ShouldReturnUserProfileDto()
    {
        var user = User.Create("query@example.com", "Query User", phoneNumber: "0123456789");
        var users = new List<User> { user };
        _dbContextMock.Setup(db => db.Users).Returns(DbSetMockHelper.CreateDbSetMock(users));

        var handler = new GetUserProfileQueryHandler(_dbContextMock.Object);
        var query = new GetUserProfileQuery(Id: user.Id, FirebaseUid: null);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be(user.Id);
        result.Email.Should().Be("query@example.com");
        result.Username.Should().Be("Query User");
        result.PhoneNumber.Should().Be("0123456789");
    }
}
