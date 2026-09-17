using PanelForge.Domain.Entities.Content;

namespace PanelForge.Application.Interfaces;

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

    /// <summary>
    /// Append các uncommitted events từ Page aggregate vào Marten stream.
    /// KHÔNG UPDATE/DELETE bất kỳ row nào — chỉ APPEND.
    /// </summary>
    Task SaveAsync(Page page, CancellationToken ct = default);
}
