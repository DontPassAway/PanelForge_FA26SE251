using FluentAssertions;
using Moq;
using PanelForge.Application.Features.Elements.AddElement;
using PanelForge.Application.Interfaces;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.Application.Elements;

public class AddElementCommandHandlerTests
{
    private readonly Mock<IPageRepository> _pageRepositoryMock;
    private readonly AddElementCommandHandler _handler;

    public AddElementCommandHandlerTests()
    {
        _pageRepositoryMock = new Mock<IPageRepository>();
        _handler = new AddElementCommandHandler(_pageRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_WhenPageExists_ShouldAddElement_SavePage_AndReturnSuccess()
    {
        // Arrange
        var page = Page.Create(Guid.NewGuid(), 1, LayoutFormat.StandardPage, 1000, 1500, 300);
        _pageRepositoryMock.Setup(r => r.GetAsync(page.Id, It.IsAny<CancellationToken>()))
                           .ReturnsAsync(page);

        var command = new AddElementCommand(
            PageId: page.Id,
            ElementType: "DialogueBalloon",
            X: 100,
            Y: 150,
            Width: 200,
            Height: 80,
            ZIndex: 1,
            Content: "Demo dialogue",
            AssetId: null,
            AddedByUserId: Guid.NewGuid());

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.PageId.Should().Be(page.Id);
        result.Value.ElementType.Should().Be("DialogueBalloon");
        result.Value.ElementId.Should().NotBeEmpty();

        _pageRepositoryMock.Verify(r => r.SaveAsync(page, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenPageDoesNotExist_ShouldReturnFailure()
    {
        // Arrange
        var nonExistentPageId = Guid.NewGuid();
        _pageRepositoryMock.Setup(r => r.GetAsync(nonExistentPageId, It.IsAny<CancellationToken>()))
                           .ReturnsAsync((Page?)null);

        var command = new AddElementCommand(
            PageId: nonExistentPageId,
            ElementType: "ArtworkLayer",
            X: 0, Y: 0, Width: 100, Height: 100, ZIndex: 0,
            Content: null, AssetId: "asset-1", AddedByUserId: Guid.NewGuid());

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("không tồn tại");
        _pageRepositoryMock.Verify(r => r.SaveAsync(It.IsAny<Page>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenInvalidDimension_ShouldReturnFailure_WithoutSaving()
    {
        // Arrange
        var page = Page.Create(Guid.NewGuid(), 1, LayoutFormat.StandardPage, 1000, 1500, 300);
        _pageRepositoryMock.Setup(r => r.GetAsync(page.Id, It.IsAny<CancellationToken>()))
                           .ReturnsAsync(page);

        var command = new AddElementCommand(
            PageId: page.Id,
            ElementType: "ArtworkLayer",
            X: 10, Y: 10, Width: -50, Height: 100, ZIndex: 1,
            Content: null, AssetId: null, AddedByUserId: Guid.NewGuid());

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Width và Height phải > 0");
        _pageRepositoryMock.Verify(r => r.SaveAsync(It.IsAny<Page>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
