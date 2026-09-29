namespace PanelForge.Application.Common.Interfaces.Persistence;

/// <summary>
/// Chạy một thao tác ghi trong MỘT transaction PostgreSQL dùng chung cho EF Core và Marten.
/// Bắt buộc dùng khi một Command ghi cả Event Store (IPageRepository) lẫn bảng quan hệ,
/// để hai nơi cùng commit hoặc cùng rollback.
///
/// Toàn bộ thao tác (load → domain method → save) phải nằm trong <paramref name="operation"/>
/// vì nó có thể được chạy lại khi EF retry lỗi kết nối tạm thời.
/// Commit khi operation kết thúc bình thường; rollback khi operation ném exception.
/// </summary>
public interface IUnitOfWork
{
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}
