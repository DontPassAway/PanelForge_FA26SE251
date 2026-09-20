using FluentAssertions;
using Moq;
using PanelForge.Application.Features.ContentElements.Commands.CreateElement;
using PanelForge.Application.Features.ContentElements.Commands.UpdateElement;
using PanelForge.Application.Features.Panels.Commands.CreatePanel;
using PanelForge.Application.Features.Panels.Commands.UpdatePanelCoordinates;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.PanelAndElementTests;

public class PanelAndElementCommandTests
{
    private readonly Mock<IPanelForgeDbContext> _dbContextMock;

    public PanelAndElementCommandTests()
    {
        _dbContextMock = new Mock<IPanelForgeDbContext>();
    }

    [Fact]
    public async Task CreatePanel_WhenPageExists_ShouldCreateWithPolygonCoordinates()
    {
        // Arrange
        var pageId = Guid.NewGuid();
        var page = Page.CreateNew(Guid.NewGuid(), 1, LayoutFormat.StandardPage, 1200, 1800, 300);
        typeof(Page).GetProperty("Id")!.SetValue(page, pageId);

        var pagesDbSet = DbSetMockHelper.CreateDbSetMock(new List<Page> { page });
        var panelsDbSet = DbSetMockHelper.CreateDbSetMock(new List<Panel>());

        _dbContextMock.Setup(db => db.Pages).Returns(pagesDbSet);
        _dbContextMock.Setup(db => db.Panels).Returns(panelsDbSet);
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CreatePanelCommandHandler(_dbContextMock.Object);
        var command = new CreatePanelCommand(
            PageId: pageId,
            UserId: Guid.NewGuid(),
            PanelNumber: 1,
            ReadingOrder: 1,
            BoundingBox: "{\"points\":[{\"x\":0,\"y\":0},{\"x\":100,\"y\":0},{\"x\":100,\"y\":100},{\"x\":0,\"y\":100}]}"
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.PanelNumber.Should().Be(1);
        result.Value.BoundingBox.Should().Contain("points");
    }

    [Fact]
    public async Task UpdatePanelCoordinates_ShouldUpdatePolygonAndReadingOrder()
    {
        // Arrange
        var panelId = Guid.NewGuid();
        var panel = Panel.Create(Guid.NewGuid(), 1, 1, "{}");
        typeof(Panel).GetProperty("Id")!.SetValue(panel, panelId);

        var panelsDbSet = DbSetMockHelper.CreateDbSetMock(new List<Panel> { panel });
        _dbContextMock.Setup(db => db.Panels).Returns(panelsDbSet);
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new UpdatePanelCoordinatesCommandHandler(_dbContextMock.Object);
        var command = new UpdatePanelCoordinatesCommand(
            PanelId: panelId,
            UserId: Guid.NewGuid(),
            BoundingBox: "{\"x\":10,\"y\":20,\"w\":300,\"h\":400}",
            ReadingOrder: 2
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.BoundingBox.Should().Be("{\"x\":10,\"y\":20,\"w\":300,\"h\":400}");
        result.Value.ReadingOrder.Should().Be(2);
    }

    [Fact]
    public async Task CreateContentElement_WhenPanelExists_ShouldCreateElementInPanel()
    {
        // Arrange
        var panelId = Guid.NewGuid();
        var panel = Panel.Create(Guid.NewGuid(), 1, 1);
        typeof(Panel).GetProperty("Id")!.SetValue(panel, panelId);

        var panelsDbSet = DbSetMockHelper.CreateDbSetMock(new List<Panel> { panel });
        var elementsDbSet = DbSetMockHelper.CreateDbSetMock(new List<Element>());

        _dbContextMock.Setup(db => db.Panels).Returns(panelsDbSet);
        _dbContextMock.Setup(db => db.Elements).Returns(elementsDbSet);
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CreateContentElementCommandHandler(_dbContextMock.Object);
        var command = new CreateContentElementCommand(
            PanelId: panelId,
            UserId: Guid.NewGuid(),
            ElementType: ElementType.DialogueBalloon,
            ZIndex: 3,
            Content: "Tôi sẽ trở thành Vua Hải Tặc!",
            TransformGeometry: "{\"x\":50,\"y\":100}",
            StyleProperties: "{\"font\":\"AnimeAce\"}",
            ScriptLineId: null,
            SpeakerCharacterId: null
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Content.Should().Be("Tôi sẽ trở thành Vua Hải Tặc!");
        result.Value.ElementType.Should().Be(ElementType.DialogueBalloon);
        result.Value.ZIndex.Should().Be(3);
    }

    [Fact]
    public async Task UpdateContentElement_ShouldUpdateTransformAndContentUrl()
    {
        // Arrange
        var elementId = Guid.NewGuid();
        var element = Element.Create(Guid.NewGuid(), ElementType.ArtworkLayer, 1, "https://storage.panelforge.com/raw.png");
        typeof(Element).GetProperty("Id")!.SetValue(element, elementId);

        var elementsDbSet = DbSetMockHelper.CreateDbSetMock(new List<Element> { element });
        _dbContextMock.Setup(db => db.Elements).Returns(elementsDbSet);
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new UpdateContentElementCommandHandler(_dbContextMock.Object);
        var command = new UpdateContentElementCommand(
            ElementId: elementId,
            UserId: Guid.NewGuid(),
            Content: "https://storage.panelforge.com/upscaled.png",
            TransformGeometry: "{\"scale\":1.5}",
            StyleProperties: "{\"opacity\":0.9}",
            ZIndex: 2,
            ScriptLineId: null,
            SpeakerCharacterId: null
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Content.Should().Be("https://storage.panelforge.com/upscaled.png");
        result.Value.TransformGeometry.Should().Be("{\"scale\":1.5}");
        result.Value.ZIndex.Should().Be(2);
    }
}
