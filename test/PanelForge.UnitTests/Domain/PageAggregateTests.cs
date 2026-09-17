using FluentAssertions;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.Domain.Events;
using Xunit;

namespace PanelForge.UnitTests.Domain;

public class PageAggregateTests
{
    private static Page CreateTestPage()
    {
        var page = Page.Create(
            chapterId: Guid.NewGuid(),
            pageNumber: 1,
            layoutFormat: LayoutFormat.WebtoonLongstrip,
            widthPx: 800,
            heightPx: 1200,
            dpi: 300);
        page.ClearUncommittedEvents();
        return page;
    }

    [Fact]
    public void Create_ValidParameters_ShouldInstantiatePageAndRaisePageCreatedEvent()
    {
        // Arrange & Act
        var chapterId = Guid.NewGuid();
        var page = Page.Create(chapterId, 1, LayoutFormat.StandardPage, 1200, 1800, 350);

        // Assert
        page.Should().NotBeNull();
        page.Id.Should().NotBeEmpty();
        page.ChapterId.Should().Be(chapterId);
        page.PageNumber.Should().Be(1);
        page.LayoutFormat.Should().Be(LayoutFormat.StandardPage);
        page.WidthPx.Should().Be(1200);
        page.HeightPx.Should().Be(1800);
        page.Dpi.Should().Be(350);
        page.Elements.Should().BeEmpty();

        // Event Sourcing check
        page.UncommittedEvents.Should().HaveCount(1);
        var createdEvent = page.UncommittedEvents[0].Should().BeOfType<PageCreatedEvent>().Subject;
        createdEvent.PageId.Should().Be(page.Id);
        createdEvent.ChapterId.Should().Be(chapterId);
        createdEvent.PageNumber.Should().Be(1);
        createdEvent.LayoutFormat.Should().Be(LayoutFormat.StandardPage);
    }

    [Theory]
    [InlineData(0, 100, 100, 300)]
    [InlineData(1, 0, 100, 300)]
    [InlineData(1, 100, -10, 300)]
    [InlineData(1, 100, 100, 0)]
    public void Create_InvalidParameters_ShouldThrowArgumentOutOfRangeException(
        int pageNumber, int width, int height, int dpi)
    {
        // Act
        Action act = () => Page.Create(Guid.NewGuid(), pageNumber, LayoutFormat.StandardPage, width, height, dpi);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AddElement_ValidInput_ShouldRaiseElementAddedEvent_AndApplyState()
    {
        // Arrange
        var page = CreateTestPage();
        var userId = Guid.NewGuid();

        // Act
        page.AddElement(
            elementType: "DialogueBalloon",
            x: 100,
            y: 200,
            width: 150,
            height: 80,
            zIndex: 1,
            addedByUserId: userId,
            content: "Hello World",
            assetId: "bubble-01");

        // Assert: UncommittedEvents có đúng 1 event
        page.UncommittedEvents.Should().HaveCount(1);
        var @event = page.UncommittedEvents[0].Should().BeOfType<ElementAddedEvent>().Subject;
        @event.PageId.Should().Be(page.Id);
        @event.ElementType.Should().Be("DialogueBalloon");
        @event.X.Should().Be(100);
        @event.Y.Should().Be(200);
        @event.Width.Should().Be(150);
        @event.Height.Should().Be(80);
        @event.Content.Should().Be("Hello World");
        @event.AddedByUserId.Should().Be(userId);

        // Assert: In-memory state đã được cập nhật
        page.Elements.Should().HaveCount(1);
        var state = page.Elements.Values.First();
        state.ElementId.Should().Be(@event.ElementId);
        state.ElementType.Should().Be("DialogueBalloon");
        state.Content.Should().Be("Hello World");
        state.IsRemoved.Should().BeFalse();
    }

    [Fact]
    public void AddElement_EmptyElementType_ShouldThrowArgumentException()
    {
        // Arrange
        var page = CreateTestPage();

        // Act
        Action act = () => page.AddElement(
            elementType: "",
            x: 10, y: 10, width: 50, height: 50, zIndex: 0,
            addedByUserId: Guid.NewGuid());

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*ElementType*");
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(50, 0)]
    [InlineData(-10, 50)]
    public void AddElement_InvalidDimensions_ShouldThrowArgumentException(double width, double height)
    {
        // Arrange
        var page = CreateTestPage();

        // Act
        Action act = () => page.AddElement(
            elementType: "ArtworkLayer",
            x: 10, y: 10, width: width, height: height, zIndex: 0,
            addedByUserId: Guid.NewGuid());

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("Width và Height phải > 0.");
    }

    [Fact]
    public void MoveElement_ValidPosition_ShouldRaiseElementMovedEvent_WithPreviousAndNewPositions()
    {
        // Arrange
        var page = CreateTestPage();
        var userId = Guid.NewGuid();
        page.AddElement("SoundEffect", 50, 60, 100, 100, 1, userId);
        var elementId = page.Elements.Keys.First();
        page.ClearUncommittedEvents();

        // Act
        page.MoveElement(elementId, newX: 120, newY: 180, newWidth: 150, newHeight: 150, movedByUserId: userId);

        // Assert
        page.UncommittedEvents.Should().HaveCount(1);
        var movedEvent = page.UncommittedEvents[0].Should().BeOfType<ElementMovedEvent>().Subject;
        movedEvent.ElementId.Should().Be(elementId);
        movedEvent.PreviousX.Should().Be(50);
        movedEvent.PreviousY.Should().Be(60);
        movedEvent.NewX.Should().Be(120);
        movedEvent.NewY.Should().Be(180);
        movedEvent.NewWidth.Should().Be(150);
        movedEvent.NewHeight.Should().Be(150);
        movedEvent.MovedByUserId.Should().Be(userId);

        // State verify
        var state = page.Elements[elementId];
        state.X.Should().Be(120);
        state.Y.Should().Be(180);
        state.Width.Should().Be(150);
        state.Height.Should().Be(150);
    }

    [Fact]
    public void MoveElement_SamePosition_ShouldBeIdempotent_AndNotRaiseEvent()
    {
        // Arrange
        var page = CreateTestPage();
        var userId = Guid.NewGuid();
        page.AddElement("SoundEffect", 50, 60, 100, 100, 1, userId);
        var elementId = page.Elements.Keys.First();
        page.ClearUncommittedEvents();

        // Act: Move to exact same position
        page.MoveElement(elementId, newX: 50, newY: 60, newWidth: 100, newHeight: 100, movedByUserId: userId);

        // Assert: No new events raised
        page.UncommittedEvents.Should().BeEmpty();
    }

    [Fact]
    public void MoveElement_NonExistentElement_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var page = CreateTestPage();

        // Act
        Action act = () => page.MoveElement(Guid.NewGuid(), 10, 10, 50, 50, Guid.NewGuid());

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*không tồn tại*");
    }

    [Fact]
    public void RemoveElement_ValidElement_ShouldRaiseElementRemovedEvent_AndMarkRemoved()
    {
        // Arrange
        var page = CreateTestPage();
        var userId = Guid.NewGuid();
        page.AddElement("NarrationBox", 20, 20, 80, 40, 1, userId);
        var elementId = page.Elements.Keys.First();
        page.ClearUncommittedEvents();

        // Act
        page.RemoveElement(elementId, reason: "Redundant narration", removedByUserId: userId);

        // Assert
        page.UncommittedEvents.Should().HaveCount(1);
        var removedEvent = page.UncommittedEvents[0].Should().BeOfType<ElementRemovedEvent>().Subject;
        removedEvent.ElementId.Should().Be(elementId);
        removedEvent.Reason.Should().Be("Redundant narration");
        removedEvent.RemovedByUserId.Should().Be(userId);

        // Element state is soft-deleted (IsRemoved == true)
        page.Elements[elementId].IsRemoved.Should().BeTrue();
    }

    [Fact]
    public void RemoveElement_AlreadyRemoved_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var page = CreateTestPage();
        var userId = Guid.NewGuid();
        page.AddElement("NarrationBox", 20, 20, 80, 40, 1, userId);
        var elementId = page.Elements.Keys.First();
        page.RemoveElement(elementId, "First delete", userId);

        // Act: Thử xóa lần 2
        Action act = () => page.RemoveElement(elementId, "Second delete", userId);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*đã bị xóa*");
    }

    [Fact]
    public void Apply_ReplayEvents_ShouldReconstructFullAggregateState()
    {
        // Arrange: Giả lập replay stream từ Marten Event Store
        var page = CreateTestPage();
        var elementId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var event1 = new ElementAddedEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTimeOffset.UtcNow,
            PageId: page.Id,
            ElementId: elementId,
            ElementType: "DialogueBalloon",
            ZIndex: 1,
            X: 10, Y: 20, Width: 100, Height: 50,
            Content: "Original text",
            AssetId: null,
            AddedByUserId: userId);

        var event2 = new ElementMovedEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTimeOffset.UtcNow,
            PageId: page.Id,
            ElementId: elementId,
            PreviousX: 10, PreviousY: 20,
            NewX: 80, NewY: 150,
            NewWidth: 120, NewHeight: 60,
            MovedByUserId: userId);

        // Act: Apply theo thứ tự
        page.Apply(event1);
        page.Apply(event2);

        // Assert
        page.Elements.Should().ContainKey(elementId);
        var reconstructed = page.Elements[elementId];
        reconstructed.X.Should().Be(80);
        reconstructed.Y.Should().Be(150);
        reconstructed.Width.Should().Be(120);
        reconstructed.Height.Should().Be(60);
        reconstructed.Content.Should().Be("Original text");
        reconstructed.IsRemoved.Should().BeFalse();
    }

    [Fact]
    public void UpdateCanvasSettings_ValidInput_ShouldRaisePageCanvasUpdatedEvent()
    {
        // Arrange
        var page = CreateTestPage();

        // Act
        page.UpdateCanvasSettings(1600, 2400, 600, LayoutFormat.WebtoonLongstrip);

        // Assert
        page.UncommittedEvents.Should().HaveCount(1);
        var @event = page.UncommittedEvents[0].Should().BeOfType<PageCanvasUpdatedEvent>().Subject;
        @event.PageId.Should().Be(page.Id);
        @event.WidthPx.Should().Be(1600);
        @event.HeightPx.Should().Be(2400);
        @event.Dpi.Should().Be(600);
        @event.LayoutFormat.Should().Be(LayoutFormat.WebtoonLongstrip);

        page.WidthPx.Should().Be(1600);
        page.HeightPx.Should().Be(2400);
        page.Dpi.Should().Be(600);
        page.LayoutFormat.Should().Be(LayoutFormat.WebtoonLongstrip);
    }

    [Fact]
    public void UpdateCanvasSettings_SameInput_ShouldNotRaiseEvent()
    {
        // Arrange
        var page = CreateTestPage();

        // Act
        page.UpdateCanvasSettings(page.WidthPx, page.HeightPx, page.Dpi, page.LayoutFormat);

        // Assert
        page.UncommittedEvents.Should().BeEmpty();
    }

    [Fact]
    public void Reorder_ValidInput_ShouldRaisePageReorderedEvent()
    {
        // Arrange
        var page = CreateTestPage();

        // Act
        page.Reorder(5);

        // Assert
        page.UncommittedEvents.Should().HaveCount(1);
        var @event = page.UncommittedEvents[0].Should().BeOfType<PageReorderedEvent>().Subject;
        @event.PageId.Should().Be(page.Id);
        @event.NewPageNumber.Should().Be(5);
        page.PageNumber.Should().Be(5);
    }

    [Fact]
    public void Reorder_SamePageNumber_ShouldNotRaiseEvent()
    {
        // Arrange
        var page = CreateTestPage();

        // Act
        page.Reorder(page.PageNumber);

        // Assert
        page.UncommittedEvents.Should().BeEmpty();
    }
}

