using FluentAssertions;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.Domain.Events;
using PanelForge.Infrastructure.Persistence.Projections;
using Xunit;

namespace PanelForge.UnitTests.Infrastructure.Projections;

public class PageProjectionTests
{
    private readonly PageProjection _projection = new();

    [Fact]
    public void PageProjection_ShouldApplyAllSixEventsCorrectly()
    {
        // 1. Create event
        var pageId = Guid.NewGuid();
        var chapterId = Guid.NewGuid();
        var createdEvent = new PageCreatedEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTimeOffset.UtcNow,
            PageId: pageId,
            ChapterId: chapterId,
            PageNumber: 1,
            LayoutFormat: LayoutFormat.StandardPage,
            WidthPx: 800,
            HeightPx: 1200,
            Dpi: 300,
            SceneId: null
        );

        var page = _projection.Create(createdEvent);
        page.Should().NotBeNull();
        page.Id.Should().Be(pageId);
        page.ChapterId.Should().Be(chapterId);
        page.PageNumber.Should().Be(1);
        page.WidthPx.Should().Be(800);
        page.HeightPx.Should().Be(1200);

        // 2. Canvas Updated
        var canvasEvent = new PageCanvasUpdatedEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTimeOffset.UtcNow,
            PageId: pageId,
            WidthPx: 1024,
            HeightPx: 1536,
            Dpi: 350,
            LayoutFormat: LayoutFormat.SpreadDouble
        );
        _projection.Apply(page, canvasEvent);
        page.WidthPx.Should().Be(1024);
        page.HeightPx.Should().Be(1536);
        page.Dpi.Should().Be(350);
        page.LayoutFormat.Should().Be(LayoutFormat.SpreadDouble);

        // 3. Reordered
        var reorderedEvent = new PageReorderedEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTimeOffset.UtcNow,
            PageId: pageId,
            NewPageNumber: 5
        );
        _projection.Apply(page, reorderedEvent);
        page.PageNumber.Should().Be(5);

        // 4. Element Added
        var elementId = Guid.NewGuid();
        var addedEvent = new ElementAddedEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTimeOffset.UtcNow,
            PageId: pageId,
            ElementId: elementId,
            ElementType: "Dialogue",
            ZIndex: 1,
            X: 100,
            Y: 150,
            Width: 200,
            Height: 80,
            Content: "Hello world",
            AssetId: null,
            AddedByUserId: Guid.NewGuid()
        );
        _projection.Apply(page, addedEvent);
        page.Elements.Should().ContainKey(elementId);
        page.Elements[elementId].Content.Should().Be("Hello world");
        page.Elements[elementId].IsRemoved.Should().BeFalse();

        // 5. Element Moved
        var movedEvent = new ElementMovedEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTimeOffset.UtcNow,
            PageId: pageId,
            ElementId: elementId,
            PreviousX: 100,
            PreviousY: 150,
            NewX: 200,
            NewY: 250,
            NewWidth: 220,
            NewHeight: 90,
            MovedByUserId: Guid.NewGuid()
        );
        _projection.Apply(page, movedEvent);
        page.Elements[elementId].X.Should().Be(200);
        page.Elements[elementId].Y.Should().Be(250);
        page.Elements[elementId].Width.Should().Be(220);
        page.Elements[elementId].Height.Should().Be(90);

        // 6. Element Removed
        var removedEvent = new ElementRemovedEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTimeOffset.UtcNow,
            PageId: pageId,
            ElementId: elementId,
            Reason: "USER_DELETE",
            RemovedByUserId: Guid.NewGuid()
        );
        _projection.Apply(page, removedEvent);
        page.Elements[elementId].IsRemoved.Should().BeTrue();
    }
}
