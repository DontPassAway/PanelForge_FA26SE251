using Marten;
using Marten.Events;
using PanelForge.Application.Interfaces;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Infrastructure.Repositories;

/// <summary>
/// Marten-based implementation c?a IPageRepository.
///
/// Cách Marten ho?t d?ng:
///   - M?i Page có m?t "stream" riêng trong PostgreSQL table "mt_events"
///   - Stream key = pageId (Guid)
///   - Events du?c serialized thành JSON và append vào stream
///   - Khi FetchForWritingAsync(), Marten replay t?t c? events
///     và g?i page.Apply(event) d? rebuild state in-memory
///     d?ng th?i lock stream d? tránh race condition
/// </summary>
public sealed class MartenPageRepository : IPageRepository
{
    private readonly IDocumentSession _session;

    public MartenPageRepository(IDocumentSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Load Page b?ng Marten Event Sourcing (read-only path):
    ///   1. Query: SELECT * FROM mt_events WHERE stream_id = pageId ORDER BY version
    ///   2. Instantiate Page() r?ng
    ///   3. G?i page.Apply(event) theo th? t? cho t?ng event
    ///   4. Tr? v? Page v?i d?y d? state hi?n t?i
    ///
    /// NOTE: Dùng AggregateStreamAsync cho read path (GetElements, GetPage, v.v.)
    /// </summary>
    public async Task<Page?> GetAsync(Guid pageId, CancellationToken ct = default)
    {
        // AggregateStreamAsync: Marten replay t?t c? events trong stream và
        // instantiate aggregate b?ng cách g?i Apply() cho t?ng event theo th? t?
        var page = await _session.Events.AggregateStreamAsync<Page>(pageId, token: ct);

        if (page is null)
            return null;

        // L?y version hi?n t?i t? stream metadata
        var state = await _session.Events.FetchStreamStateAsync(pageId, ct);
        if (state is not null)
            page.SetVersion(state.Version);

        return page;
    }

    /// <summary>
    /// Luu uncommitted events t? Page vào Marten stream.
    /// KHÔNG UPDATE b?t k? row nào — ch? APPEND events m?i vào mt_events.
    ///
    /// Strategy:
    ///   - Version == -1 (chua bao gi? persist) ? StartStream (t?o stream m?i)
    ///   - Version >= 0 (dã t?n t?i trong DB)   ? FetchForWriting + Append
    ///     (dùng IEventStream d? tránh session tracking conflicts)
    /// </summary>
    public async Task SaveAsync(Page page, CancellationToken ct = default)
    {
        if (!page.UncommittedEvents.Any())
            return;

        var events = page.UncommittedEvents.Cast<object>().ToArray();

        if (page.Version < 0)
        {
            // Stream m?i — kh?i t?o l?n d?u
            // StartStream s? fail v?i concurrency error n?u stream dã t?n t?i
            _session.Events.StartStream<Page>(page.Id, events);
        }
        else
        {
            // Dùng FetchForWriting d? load stream dúng cách cho write path.
            // FetchForWriting tr? v? IEventStream<Page> dã tracked b?i session,
            // tránh conflict v?i AggregateStreamAsync dã ch?y tru?c dó trong GetAsync.
            // AppendOptimistic s? check version t? d?ng.
            var stream = await _session.Events.FetchForWriting<Page>(page.Id, ct);
            stream.AppendMany(events);
        }

        // INSERT INTO mt_events (...) — DUY NH?T thao tác SQL x?y ra
        await _session.SaveChangesAsync(ct);

        // Xóa buffer — dã persist thành công
        page.ClearUncommittedEvents();
    }
}
