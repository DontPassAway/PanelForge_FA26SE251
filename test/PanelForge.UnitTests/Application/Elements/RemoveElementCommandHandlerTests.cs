using FluentAssertions;
using Moq;
using PanelForge.Application.Features.Elements.RemoveElement;
using PanelForge.Application.Interfaces;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.Application.Elements;

public class RemoveElementCommandHandlerTests
{
    private readonly Mock<IPageRepository> _pageRepositoryMock;
    private readonly RemoveElementCommandHandler _handler;

    public RemoveElementCommandHandlerTests()
    {
        _pageRepositoryMock = new Mock<IPageRepository>();
        _handler = new RemoveElementCommandHandler(_pageRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_WhenValid_ShouldRemoveElement_AndSave()
    {
        // Arrange
        var page = Page.CreateNew(Guid.NewGuid(), 1, LayoutFormat.StandardPage, 1000, 1500, 300);
        var userId = Guid.NewGuid();
        page.AddElement("NarrationBox", 10, 20, 50, 50, 1, userId);
        var elementId = page.Elements.Keys.First();

        _pageRepositoryMock.Setup(r => r.GetAsync(page.Id, It.IsAny<CancellationToken>()))
                           .ReturnsAsync(page);

        var command = new RemoveElementCommand(
            PageId: page.Id,
            ElementId: elementId,
            Reason: "Artist revised layout",
            RemovedByUserId: userId);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        _pageRepositoryMock.Verify(r => r.SaveAsync(page, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenPageNotFound_ShouldReturnFailure()
    {
        // Arrange
        var nonExistentPageId = Guid.NewGuid();
        _pageRepositoryMock.Setup(r => r.GetAsync(nonExistentPageId, It.IsAny<CancellationToken>()))
                           .ReturnsAsync((Page?)null);

        var command = new RemoveElementCommand(
            PageId: nonExistentPageId,
            ElementId: Guid.NewGuid(),
            Reason: "Delete",
            RemovedByUserId: Guid.NewGuid());

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("không tồn tại");
        _pageRepositoryMock.Verify(r => r.SaveAsync(It.IsAny<Page>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
