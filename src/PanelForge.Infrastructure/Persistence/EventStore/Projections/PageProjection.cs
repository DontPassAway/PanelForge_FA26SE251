using Marten.Events.Aggregation;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Events;

namespace PanelForge.Infrastructure.Persistence.EventStore.Projections;

public class PageProjection : SingleStreamProjection<Page, Guid>
{
    public PageProjection()
    {
    }

    public Page Create(PageCreatedEvent @event)
    {
        var page = new Page();
        page.Apply(@event);
        return page;
    }

    public void Apply(Page page, PageCanvasUpdatedEvent @event) => page.Apply(@event);
    public void Apply(Page page, PageReorderedEvent @event) => page.Apply(@event);
    public void Apply(Page page, ElementAddedEvent @event) => page.Apply(@event);
    public void Apply(Page page, ElementMovedEvent @event) => page.Apply(@event);
    public void Apply(Page page, ElementRemovedEvent @event) => page.Apply(@event);
    public void Apply(Page page, ElementTextUpdatedEvent @event) => page.Apply(@event);
    public void Apply(Page page, ElementOverflowAcknowledgedEvent @event) => page.Apply(@event);
    public void Apply(Page page, ElementReadingOrderSetEvent @event) => page.Apply(@event);
    public void Apply(Page page, ElementStyleUpdatedEvent @event) => page.Apply(@event);
    public void Apply(Page page, ElementRestoredEvent @event) => page.Apply(@event);
    public void Apply(Page page, PageDeletedEvent @event) => page.Apply(@event);
}
