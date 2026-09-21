namespace PanelForge.Domain.Common;

/// <summary>
/// Base class cho tất cả Aggregate Roots trong hệ thống Event Sourcing,
/// đồng thời hỗ trợ Audit Timestamps và Soft-delete.
/// </summary>
public abstract class AggregateRoot : IAuditableEntity, ISoftDelete
{
    // ── Identity & Versioning ───────────────────────────────────────────────
    public Guid Id { get; protected set; } = Guid.NewGuid();

    /// <summary>
    /// Version hiện tại của aggregate — dùng cho Optimistic Concurrency.
    /// Marten tự quản lý giá trị này khi load stream.
    /// </summary>
    public long Version { get; private set; } = -1; // -1 = chưa persisted

    // ── Audit fields ────────────────────────────────────────────────────────
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    // ── Soft-delete fields ──────────────────────────────────────────────────
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

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
