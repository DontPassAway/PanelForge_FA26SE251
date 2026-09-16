using FluentAssertions;
using Moq;
using PanelForge.Application.Features.Elements.MoveElement;
using PanelForge.Application.Interfaces;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.Application.Elements;

public class MoveElementCommandHandlerTests
{
    private readonly Mock<IPageRepository> _pageRepositoryMock;
    private readonly MoveElementCommandHandler _handler;

    public MoveElementCommandHandlerTests()
    {
        _pageRepositoryMock = new Mock<IPageRepository>();
        _handler = new MoveElementCommandHandler(_pageRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_WhenValid_ShouldMoveElement_AndSave()
    {
        // Arrange
        var page = Page.Create(Guid.NewGuid(), 1, LayoutFormat.StandardPage, 1000, 1500, 300);
        var userId = Guid.NewGuid();
        page.AddElement("SoundEffect", 10, 20, 50, 50, 1, userId);
        var elementId = page.Elements.Keys.First();

        _pageRepositoryMock.Setup(r => r.GetAsync(page.Id, It.IsAny<CancellationToken>()))
                           .ReturnsAsync(page);

        var command = new MoveElementCommand(
            PageId: page.Id,
            ElementId: elementId,
            NewX: 60,
            NewY: 80,
            NewWidth: 100,
            NewHeight: 100,
            ExpectedPageVersion: page.Version,
            MovedByUserId: userId);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        _pageRepositoryMock.Verify(r => r.SaveAsync(page, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenVersionMismatch_ShouldReturnFailure_OptimisticConcurrency()
    {
        // Arrange
        var page = Page.Create(Guid.NewGuid(), 1, LayoutFormat.StandardPage, 1000, 1500, 300);
        var userId = Guid.NewGuid();
        page.AddElement("SoundEffect", 10, 20, 50, 50, 1, userId);
        var elementId = page.Elements.Keys.First();

        _pageRepositoryMock.Setup(r => r.GetAsync(page.Id, It.IsAny<CancellationToken>()))
                           .ReturnsAsync(page);

        var command = new MoveElementCommand(
            PageId: page.Id,
            ElementId: elementId,
            NewX: 60, NewY: 80, NewWidth: 100, NewHeight: 100,
            ExpectedPageVersion: 999, // Sai version
            MovedByUserId: userId);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Xung đột phiên bản");
        _pageRepositoryMock.Verify(r => r.SaveAsync(It.IsAny<Page>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenElementNotFound_ShouldReturnFailure()
    {
        // Arrange
        var page = Page.Create(Guid.NewGuid(), 1, LayoutFormat.StandardPage, 1000, 1500, 300);
        _pageRepositoryMock.Setup(r => r.GetAsync(page.Id, It.IsAny<CancellationToken>()))
                           .ReturnsAsync(page);

        var nonExistentElementId = Guid.NewGuid();
        var command = new MoveElementCommand(
            PageId: page.Id,
            ElementId: nonExistentElementId,
            NewX: 60, NewY: 80, NewWidth: 100, NewHeight: 100,
            ExpectedPageVersion: page.Version,
            MovedByUserId: Guid.NewGuid());

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("không tồn tại trên Page");
        _pageRepositoryMock.Verify(r => r.SaveAsync(It.IsAny<Page>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
