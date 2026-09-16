namespace PanelForge.Domain.Common;

/// <summary>
/// Marker interface cho tất cả Domain Events.
/// Mỗi event là một sự kiện đã xảy ra — bất biến, không thể xóa.
/// </summary>
public interface IDomainEvent
{
    /// <summary>ID duy nhất của event này.</summary>
    Guid EventId { get; }

    /// <summary>Thời điểm event xảy ra (UTC).</summary>
    DateTimeOffset OccurredAt { get; }
}
