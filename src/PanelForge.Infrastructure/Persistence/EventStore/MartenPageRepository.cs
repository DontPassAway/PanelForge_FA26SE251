using PanelForge.Application.Common.Interfaces.Persistence;
using Marten;
using Marten.Services;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Domain.Entities.Content;
using PanelForge.Infrastructure.Persistence;

namespace PanelForge.Infrastructure.Persistence.EventStore;

/// <summary>
/// Marten-based implementation của IPageRepository.
///
/// Cách Marten hoạt động:
///   - Mỗi Page có một "stream" riêng trong PostgreSQL table "mt_events"
///   - Stream key = pageId (Guid)
///   - Events được serialize thành JSON và append vào stream
///   - Khi load, Marten replay tất cả events và gọi page.Apply(event) để rebuild state
///
/// Transaction:
///   - Nếu EF Core đang có transaction (mở qua IUnitOfWork) → Marten enlist vào ĐÚNG transaction đó,
///     EF và Marten cùng commit / rollback.
///   - Nếu không → dùng session mặc định, Marten tự commit (chỉ an toàn khi Command KHÔNG ghi EF).
/// </summary>
public sealed class MartenPageRepository : IPageRepository
{
    private readonly IDocumentSession _session;
    private readonly IDocumentStore _store;
    private readonly PanelForgeDbContext _dbContext;

    public MartenPageRepository(
        IDocumentSession session,
        IDocumentStore store,
        PanelForgeDbContext dbContext)
    {
        _session = session;
        _store = store;
        _dbContext = dbContext;
    }

    /// <summary>
    /// Load Page bằng cách replay event stream.
    /// Trả về null nếu stream không tồn tại hoặc Page đã bị xóa (PageDeletedEvent).
    /// </summary>
    public async Task<Page?> GetAsync(Guid pageId, CancellationToken ct = default)
    {
        var page = await _session.Events.AggregateStreamAsync<Page>(pageId, token: ct);

        if (page is null || page.IsDeleted)
            return null;

        // Lấy version hiện tại từ stream metadata
        var state = await _session.Events.FetchStreamStateAsync(pageId, ct);
        if (state is not null)
            page.SetVersion(state.Version);

        return page;
    }

    /// <summary>
    /// Append uncommitted events của Page vào Marten stream. KHÔNG update row nào.
    ///   - Version == -1 (chưa từng persist) → StartStream
    ///   - Version >= 0                      → FetchForWriting + Append
    /// </summary>
    public async Task SaveAsync(Page page, CancellationToken ct = default)
    {
        if (!page.UncommittedEvents.Any())
            return;

        var efTransaction = _dbContext.Database.CurrentTransaction;
        var appended = page.UncommittedEvents.Count;

        try
        {
            if (efTransaction is null)
            {
                await AppendAndSaveAsync(_session, page, ct);
            }
            else
            {
                // shouldAutoCommit: false → Marten không commit; transaction do IUnitOfWork quản lý
                var npgsqlTransaction = (NpgsqlTransaction)efTransaction.GetDbTransaction();
                await using var session = _store.LightweightSession(
                    SessionOptions.ForTransaction(npgsqlTransaction, shouldAutoCommit: false));

                await AppendAndSaveAsync(session, page, ct);
            }
        }
        catch (Exception ex) when (IsStreamConcurrencyFailure(ex))
        {
            throw new ConcurrentPageUpdateException(page.Id, ex);
        }

        // Giữ Version khớp stream sau khi lưu để lần SaveAsync tiếp theo của cùng instance không tự xung đột
        page.SetVersion(Math.Max(page.Version, 0) + appended);
        page.ClearUncommittedEvents();
    }

    private static async Task AppendAndSaveAsync(IDocumentSession session, Page page, CancellationToken ct)
    {
        var events = page.UncommittedEvents.Cast<object>().ToArray();

        if (page.Version < 0)
        {
            // StartStream fail với concurrency error nếu stream đã tồn tại
            session.Events.StartStream<Page>(page.Id, events);
        }
        else
        {
            // Expected version = version lúc handler load Page: nếu request khác đã append từ đó thì Marten từ chối
            // (unique (stream_id, version) trong mt_events). Đây là chốt chặn mức DB cho BR-05 khi hai request cùng gắn một ScriptLine.
            var stream = await session.Events.FetchForWriting<Page>(page.Id, page.Version, ct);
            stream.AppendMany(events);
        }

        await session.SaveChangesAsync(ct);
    }

    // Tên kiểu thay vì catch trực tiếp: Marten đổi tên/namespace các exception này giữa các phiên bản lớn
    private static bool IsStreamConcurrencyFailure(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            var name = e.GetType().Name;
            if (name.Contains("Concurrency") || name.Contains("UnexpectedMaxEventId")) return true;
        }
        return false;
    }
}
