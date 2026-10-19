using FluentAssertions;
using PanelForge.Application.Common.Interfaces.Persistence;
using Moq;
using PanelForge.Application.Features.CanvasElements.Queries.GetPageSnapshot;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.CanvasElements;

public class GetPageSnapshotQueryHandlerTests
{
    private readonly Mock<IPageRepository> _pageRepositoryMock;
    private readonly GetPageSnapshotQueryHandler _handler;

    public GetPageSnapshotQueryHandlerTests()
    {
        _pageRepositoryMock = new Mock<IPageRepository>();
        _handler = new GetPageSnapshotQueryHandler(_pageRepositoryMock.Object, InMemoryDb.Create());
    }

    [Fact]
    public async Task Handle_WhenPageExists_ShouldReturnOnlyActiveElementsByDefault()
    {
        // Arrange
        var page = Page.CreateNew(Guid.NewGuid(), 1, LayoutFormat.StandardPage, 1000, 1500, 300);
        var userId = Guid.NewGuid();

        page.AddElement("DialogueBalloon", 10, 10, 100, 50, 1, userId, "Active 1");
        page.AddElement("ArtworkLayer", 0, 0, 1000, 1500, 0, userId);
        page.AddElement("NarrationBox", 20, 20, 80, 40, 2, userId, "Will be removed");

        var removedId = page.Elements.Values.First(e => e.Content == "Will be removed").ElementId;
        page.RemoveElement(removedId, "Soft delete", userId);

        _pageRepositoryMock.Setup(r => r.GetAsync(page.Id, It.IsAny<CancellationToken>()))
                           .ReturnsAsync(page);

        var query = new GetPageSnapshotQuery(PageId: page.Id, IncludeRemoved: false);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Elements.Should().HaveCount(2); // Only active elements
        result.Value.Elements.Should().NotContain(e => e.ElementId == removedId);
        // Kiểm tra thứ tự sắp xếp theo ZIndex tăng dần (0 rồi đến 1)
        result.Value.Elements[0].ZIndex.Should().Be(0);
        result.Value.Elements[1].ZIndex.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WithAtVersion_ReplaysStreamToThatVersion()
    {
        var page = Page.CreateNew(Guid.NewGuid(), 1, LayoutFormat.StandardPage, 1000, 1500, 300);
        page.AddElement("DialogueBalloon", 10, 10, 100, 50, 1, Guid.NewGuid(), "Bản đã nộp");
        _pageRepositoryMock.Setup(r => r.GetAtVersionAsync(page.Id, 7, It.IsAny<CancellationToken>()))
                           .ReturnsAsync(page);

        var result = await _handler.Handle(new GetPageSnapshotQuery(page.Id, AtVersion: 7), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Elements.Should().ContainSingle(e => e.Content == "Bản đã nộp");
        _pageRepositoryMock.Verify(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenIncludeRemovedIsTrue_ShouldReturnAllElements()
    {
        // Arrange
        var page = Page.CreateNew(Guid.NewGuid(), 1, LayoutFormat.StandardPage, 1000, 1500, 300);
        var userId = Guid.NewGuid();
        page.AddElement("DialogueBalloon", 10, 10, 100, 50, 1, userId);
        var elementId = page.Elements.Keys.First();
        page.RemoveElement(elementId, "Soft delete", userId);

        _pageRepositoryMock.Setup(r => r.GetAsync(page.Id, It.IsAny<CancellationToken>()))
                           .ReturnsAsync(page);

        var query = new GetPageSnapshotQuery(PageId: page.Id, IncludeRemoved: true);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Elements.Should().HaveCount(1);
        result.Value.Elements[0].IsRemoved.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenPageNotFound_ShouldReturnFailure()
    {
        // Arrange
        var nonExistentPageId = Guid.NewGuid();
        _pageRepositoryMock.Setup(r => r.GetAsync(nonExistentPageId, It.IsAny<CancellationToken>()))
                           .ReturnsAsync((Page?)null);

        var query = new GetPageSnapshotQuery(PageId: nonExistentPageId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("không tồn tại");
    }
}
