using Marten;
using Marten.Events;
using PanelForge.Application.Interfaces;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Infrastructure.Repositories;

/// <summary>
/// Marten-based implementation của IPageRepository.
///
/// Cách Marten hoạt động:
///   - Mỗi Page có một "stream" riêng trong PostgreSQL table "mt_events"
///   - Stream key = pageId (Guid)
///   - Events được serialized thành JSON và append vào stream
///   - Khi FetchForWritingAsync(), Marten replay tất cả events
///     và gọi page.Apply(event) để rebuild state in-memory
/// </summary>
public sealed class MartenPageRepository : IPageRepository
{
    private readonly IDocumentSession _session;

    public MartenPageRepository(IDocumentSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Load Page bằng Marten Event Sourcing:
    ///   1. Query: SELECT * FROM mt_events WHERE stream_id = pageId ORDER BY version
    ///   2. Instantiate Page() rỗng
    ///   3. Gọi page.Apply(event) theo thứ tự cho từng event
    ///   4. Trả về Page với đầy đủ state hiện tại
    /// </summary>
    public async Task<Page?> GetAsync(Guid pageId, CancellationToken ct = default)
    {
        // AggregateStreamAsync: Marten replay tất cả events trong stream và
        // instantiate aggregate bằng cách gọi Apply() cho từng event theo thứ tự
        var page = await _session.Events.AggregateStreamAsync<Page>(pageId, token: ct);

        if (page is null)
            return null;

        // Lấy version hiện tại từ stream metadata
        var state = await _session.Events.FetchStreamStateAsync(pageId, ct);
        if (state is not null)
            page.SetVersion(state.Version);

        return page;
    }

    /// <summary>
    /// Lưu uncommitted events từ Page vào Marten stream.
    /// KHÔNG UPDATE bất kỳ row nào — chỉ APPEND events mới vào mt_events.
    /// </summary>
    public async Task SaveAsync(Page page, CancellationToken ct = default)
    {
        if (!page.UncommittedEvents.Any())
            return;

        var events = page.UncommittedEvents.Cast<object>().ToArray();

        if (page.Version < 0)
        {
            // Stream mới — khởi tạo lần đầu
            _session.Events.StartStream<Page>(page.Id, events);
        }
        else
        {
            // Append vào stream đã có, với Optimistic Concurrency check
            _session.Events.Append(page.Id, page.Version, events);
        }

        // INSERT INTO mt_events (...) — DUY NHẤT thao tác SQL xảy ra
        await _session.SaveChangesAsync(ct);

        // Xóa buffer — đã persist thành công
        page.ClearUncommittedEvents();
    }
}
