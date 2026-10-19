using PanelForge.Domain.Entities.Content;

namespace PanelForge.Application.Common.Interfaces.Persistence;

/// <summary>
/// Repository interface cho Page Aggregate.
/// Implementation dùng Marten Event Store.
/// </summary>
public interface IPageRepository
{
    /// <summary>
    /// Load Page aggregate bằng cách replay toàn bộ event stream từ Marten.
    /// Trả về null nếu stream không tồn tại.
    /// </summary>
    Task<Page?> GetAsync(Guid pageId, CancellationToken ct = default);

    /// <summary>Page dựng lại tới đúng <paramref name="version"/> của stream (xem lại bản đã nộp, BR-01). Null nếu không có.</summary>
    Task<Page?> GetAtVersionAsync(Guid pageId, long version, CancellationToken ct = default);

    /// <summary>Version hiện tại của event stream (không replay); -1 nếu Page chưa có event nào.</summary>
    Task<long> GetStreamVersionAsync(Guid pageId, CancellationToken ct = default);

    /// <summary>Toàn bộ event của Page theo thứ tự (chỉ đọc, append-only) — dùng cho truy vết CF4. Rỗng nếu chưa có stream.</summary>
    Task<IReadOnlyList<PanelForge.Domain.Common.IDomainEvent>> GetEventsAsync(Guid pageId, CancellationToken ct = default);

    /// <summary>
    /// Append các uncommitted events từ Page aggregate vào Marten stream.
    /// KHÔNG UPDATE/DELETE bất kỳ row nào — chỉ APPEND.
    /// Ném <see cref="ConcurrentPageUpdateException"/> nếu stream đã có event mới kể từ lúc load (optimistic concurrency).
    /// </summary>
    Task SaveAsync(Page page, CancellationToken ct = default);
}

/// <summary>Stream của Page đã đổi giữa lúc load và lúc lưu: các luật kiểm trên bản đã load (vd BR-05) có thể không còn đúng.</summary>
public sealed class ConcurrentPageUpdateException : Exception
{
    public ConcurrentPageUpdateException(Guid pageId, Exception? inner = null)
        : base($"Xung đột phiên bản: Page {pageId} vừa được người khác cập nhật. Vui lòng tải lại và thử lại.", inner) { }
}
