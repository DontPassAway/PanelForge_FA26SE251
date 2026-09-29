using FluentAssertions;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.Domain.Events;
using Xunit;

namespace PanelForge.UnitTests.Domain;

public class PageDeleteTests
{
    private static Page CreatePersistedPage()
    {
        var page = Page.CreateNew(Guid.NewGuid(), 1, LayoutFormat.StandardPage, 1200, 1800, 300);
        page.ClearUncommittedEvents();
        return page;
    }

    [Fact]
    public void Delete_ShouldMarkDeletedAndRaisePageDeletedEvent()
    {
        var page = CreatePersistedPage();
        var userId = Guid.NewGuid();

        page.Delete(userId);

        page.IsDeleted.Should().BeTrue();
        page.DeletedBy.Should().Be(userId.ToString());
        page.DeletedAt.Should().NotBeNull();
        var evt = page.UncommittedEvents.Should().ContainSingle().Which.Should().BeOfType<PageDeletedEvent>().Subject;
        evt.PageId.Should().Be(page.Id);
        evt.DeletedByUserId.Should().Be(userId);
    }

    [Fact]
    public void Delete_WhenAlreadyDeleted_ShouldThrow()
    {
        var page = CreatePersistedPage();
        page.Delete(null);

        var act = () => page.Delete(null);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Replay_WithPageDeletedEvent_ShouldRebuildDeletedState()
    {
        var original = Page.CreateNew(Guid.NewGuid(), 1, LayoutFormat.StandardPage, 1200, 1800, 300);
        original.Delete(Guid.NewGuid());

        var replayed = (Page)Activator.CreateInstance(typeof(Page), nonPublic: true)!;
        foreach (var e in original.UncommittedEvents)
            replayed.Apply(e);

        replayed.Id.Should().Be(original.Id);
        replayed.IsDeleted.Should().BeTrue();
    }
}
