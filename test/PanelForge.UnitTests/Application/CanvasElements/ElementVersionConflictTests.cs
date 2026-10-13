using PanelForge.UnitTests.Common;
using FluentAssertions;
using Moq;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.CanvasElements.Commands.AddElement;
using PanelForge.Application.Features.CanvasElements.Commands.MoveElement;
using PanelForge.Application.Features.CanvasElements.Commands.RemoveElement;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.Application.CanvasElements;

/// <summary>BR-11: chỉ báo xung đột khi hai người sửa CÙNG element (theo version riêng của element).</summary>
public class ElementVersionConflictTests
{
    private readonly Mock<IPageRepository> _repo = new();
    private readonly Guid _user = Guid.NewGuid();

    private (Page page, Guid a, Guid b) PageWithTwoElements()
    {
        var page = Page.CreateNew(Guid.NewGuid(), 1, LayoutFormat.StandardPage, 1000, 1500, 300);
        page.AddElement("CHARACTER", 10, 10, 50, 50, 1, _user);
        page.AddElement("CHARACTER", 200, 200, 50, 50, 1, _user);
        var ids = page.Elements.Keys.ToList();
        _repo.Setup(r => r.GetAsync(page.Id, It.IsAny<CancellationToken>())).ReturnsAsync(page);
        return (page, ids[0], ids[1]);
    }

    private MoveElementCommand Move(Page page, Guid id, double x, int? expected) =>
        new(page.Id, id, x, 10, 50, 50, ExpectedPageVersion: null, MovedByUserId: _user, ExpectedElementVersion: expected);

    [Fact]
    public void Version_StartsAt1_AndIncrementsOnlyForThatElement()
    {
        var (page, a, b) = PageWithTwoElements();
        page.Elements[a].Version.Should().Be(1);

        page.MoveElement(a, 20, 10, 50, 50, _user);

        page.Elements[a].Version.Should().Be(2);
        page.Elements[b].Version.Should().Be(1);
    }

    [Fact]
    public async Task Move_WhenOtherElementOnSamePageChanged_ShouldSucceed()
    {
        var (page, a, b) = PageWithTwoElements();
        page.MoveElement(b, 300, 300, 50, 50, _user); // người khác sửa element B (page version tăng)

        var result = await new MoveElementCommandHandler(_repo.Object, InMemoryDb.Create()).Handle(Move(page, a, 99, expected: 1), default);

        result.IsSuccess.Should().BeTrue();
        page.Elements[a].X.Should().Be(99);
    }

    [Fact]
    public async Task Move_WhenSameElementChangedSinceLoad_ShouldConflict_AndNotSave()
    {
        var (page, a, _) = PageWithTwoElements();
        page.MoveElement(a, 30, 10, 50, 50, _user); // người khác đã kéo A → version 2

        var result = await new MoveElementCommandHandler(_repo.Object, InMemoryDb.Create()).Handle(Move(page, a, 99, expected: 1), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain(Page.ElementVersionConflictCode);
        page.Elements[a].X.Should().Be(30);
        _repo.Verify(r => r.SaveAsync(It.IsAny<Page>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Move_WithoutAnyExpectedVersion_ShouldFail()
    {
        var (page, a, _) = PageWithTwoElements();

        var result = await new MoveElementCommandHandler(_repo.Object, InMemoryDb.Create()).Handle(Move(page, a, 99, expected: null), default);

        result.IsSuccess.Should().BeFalse();
        _repo.Verify(r => r.SaveAsync(It.IsAny<Page>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Move_WhenStreamChangedBetweenLoadAndSave_ShouldReturnConflict_Not500()
    {
        var (page, a, _) = PageWithTwoElements();
        _repo.Setup(r => r.SaveAsync(page, It.IsAny<CancellationToken>()))
             .ThrowsAsync(new ConcurrentPageUpdateException(page.Id));

        var result = await new MoveElementCommandHandler(_repo.Object, InMemoryDb.Create()).Handle(Move(page, a, 99, expected: 1), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Xung đột");
    }

    [Fact]
    public async Task AddElement_ReplayedWithSameClientId_DoesNotCreateDuplicate()
    {
        var page = Page.CreateNew(Guid.NewGuid(), 1, LayoutFormat.StandardPage, 1000, 1500, 300);
        _repo.Setup(r => r.GetAsync(page.Id, It.IsAny<CancellationToken>())).ReturnsAsync(page);
        var handler = new AddElementCommandHandler(_repo.Object, PanelForge.UnitTests.Common.InMemoryDb.Create());
        var clientId = Guid.NewGuid();
        AddElementCommand Add(string type) => new(page.Id, type, 10, 10, 50, 50, 1, null, null, _user, ClientElementId: clientId);

        var first = await handler.Handle(Add("CHARACTER"), default);
        var replay = await handler.Handle(Add("CHARACTER"), default);
        var misuse = await handler.Handle(Add("SOUND_EFFECT"), default);

        first.Value!.ElementId.Should().Be(clientId);
        first.Value.Replayed.Should().BeFalse();
        replay.Value!.Replayed.Should().BeTrue();
        page.Elements.Should().HaveCount(1);
        misuse.ErrorMessage.Should().Contain("Xung đột");
        _repo.Verify(r => r.SaveAsync(page, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Remove_WhenSameElementChangedSinceLoad_ShouldConflict()
    {
        var (page, a, _) = PageWithTwoElements();
        page.MoveElement(a, 30, 10, 50, 50, _user);

        var result = await new RemoveElementCommandHandler(_repo.Object, InMemoryDb.Create())
            .Handle(new RemoveElementCommand(page.Id, a, "USER_DELETE", _user, ExpectedElementVersion: 1), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain(Page.ElementVersionConflictCode);
        page.Elements[a].IsRemoved.Should().BeFalse();
    }
}
