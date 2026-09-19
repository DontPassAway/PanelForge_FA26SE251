namespace PanelForge.Domain.Common;

/// <summary>
/// Base class cho tất cả Aggregate Roots trong hệ thống Event Sourcing.
///
/// Cách hoạt động:
///   1. Command gọi method trên Aggregate (ví dụ: page.AddElement(...))
///   2. Method đó gọi RaiseEvent(new SomeEvent(...))
///   3. RaiseEvent tự động gọi Apply(event) để cập nhật state in-memory
///   4. Event được thêm vào _uncommittedEvents
///   5. Repository lấy _uncommittedEvents rồi append vào Event Store (Marten)
/// </summary>
public abstract class AggregateRoot
{
    // ── Identity & Versioning ───────────────────────────────────────────────
    public Guid Id { get; protected set; } = Guid.NewGuid();

    /// <summary>
    /// Version hiện tại của aggregate — dùng cho Optimistic Concurrency.
    /// Marten tự quản lý giá trị này khi load stream.
    /// </summary>
    public long Version { get; private set; } = -1; // -1 = chưa persisted

    // ── Event buffer (chưa được lưu vào Event Store) ────────────────────────
    private readonly List<IDomainEvent> _uncommittedEvents = new();

    /// <summary>Danh sách events chưa được append vào Event Store.</summary>
    public IReadOnlyList<IDomainEvent> UncommittedEvents => _uncommittedEvents.AsReadOnly();

    /// <summary>Xóa buffer sau khi Repository đã lưu thành công.</summary>
    public void ClearUncommittedEvents() => _uncommittedEvents.Clear();

    /// <summary>Marten gọi setter này khi rebuild aggregate từ event stream.</summary>
    public void SetVersion(long version) => Version = version;

    // ── Core Event Sourcing mechanism ───────────────────────────────────────

    /// <summary>
    /// Raise một domain event:
    ///   - Apply event để cập nhật state in-memory ngay lập tức
    ///   - Buffer event vào UncommittedEvents để Repository lưu sau
    /// </summary>
    protected void RaiseEvent(IDomainEvent domainEvent)
    {
        Apply(domainEvent);                  // cập nhật state ngay
        _uncommittedEvents.Add(domainEvent); // buffer để persist sau
    }

    /// <summary>
    /// Apply một event từ Event Store khi rebuild aggregate state từ lịch sử.
    /// Mỗi subclass override method này để xử lý từng event type.
    /// </summary>
    public abstract void Apply(IDomainEvent domainEvent);
}
