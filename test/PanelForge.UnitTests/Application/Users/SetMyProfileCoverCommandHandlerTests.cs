using FluentAssertions;
using Moq;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Users.Commands.SetMyProfileCover;
using PanelForge.Domain.Entities.Identity;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Users;

public class SetMyProfileCoverCommandHandlerTests
{
    private readonly Mock<IPanelForgeDbContext> _db = new();

    private SetMyProfileCoverCommandHandler HandlerWith(params User[] users)
    {
        _db.Setup(d => d.Users).Returns(DbSetMockHelper.CreateDbSetMock(users.ToList()));
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return new SetMyProfileCoverCommandHandler(_db.Object);
    }

    [Fact]
    public async Task ValidPreset_IsSavedOnCallerAndReturned()
    {
        var me = User.Create("me@test.com", "Me");
        var other = User.Create("other@test.com", "Other");
        var handler = HandlerWith(me, other);

        var dto = await handler.Handle(
            new SetMyProfileCoverCommand(me.Id, null, "profile-cover-robot-studio"), CancellationToken.None);

        dto!.ProfileCoverPresetId.Should().Be("profile-cover-robot-studio");
        me.ProfileCoverPresetId.Should().Be("profile-cover-robot-studio");
        other.ProfileCoverPresetId.Should().BeNull();
        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NullPreset_ResetsToDefault()
    {
        var me = User.Create("me@test.com", "Me");
        me.SetProfileCoverPreset("profile-cover-manga-studio");
        var handler = HandlerWith(me);

        var dto = await handler.Handle(new SetMyProfileCoverCommand(me.Id, null, null), CancellationToken.None);

        dto!.ProfileCoverPresetId.Should().BeNull();
    }

    [Fact]
    public async Task UnknownPreset_ThrowsAndDoesNotSave()
    {
        var me = User.Create("me@test.com", "Me");
        var handler = HandlerWith(me);

        var act = () => handler.Handle(
            new SetMyProfileCoverCommand(me.Id, null, "data:image/png;base64,AAAA"), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        me.ProfileCoverPresetId.Should().BeNull();
        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnknownCaller_ReturnsNull()
    {
        var handler = HandlerWith(User.Create("me@test.com", "Me"));

        var dto = await handler.Handle(
            new SetMyProfileCoverCommand(Guid.NewGuid(), null, "profile-cover-manga-studio"), CancellationToken.None);

        dto.Should().BeNull();
    }
}
