using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Workspaces;

/// <summary>
/// Yêu cầu tăng tài nguyên do Producer gửi cho Administrator (cấp phát, không mua bán).
/// Duyệt/từ chối chỉ một lần: trạng thái kiểm ở đây + xmin chặn hai Admin duyệt cùng lúc.
/// </summary>
public class ResourceRequest : BaseEntity
{
    public const int MaxReasonLength = 1000;

    public Guid WorkspaceId { get; private set; }
    public ResourceType ResourceType { get; private set; }
    /// <summary>Giới hạn tại thời điểm gửi (byte với Storage, token/ảnh mỗi tháng với AI). 0 = không giới hạn.</summary>
    public long CurrentLimit { get; private set; }
    public long RequestedLimit { get; private set; }
    public string Reason { get; private set; } = default!;
    public Guid RequestedBy { get; private set; }
    public ResourceRequestStatus Status { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public DateTime? ReviewedAt { get; private set; }
    public string? ReviewNote { get; private set; }

    /// <summary>Concurrency token ánh xạ cột hệ thống <c>xmin</c> (BR-11).</summary>
    public uint RowVersion { get; private set; }

    private ResourceRequest() { }

    public static ResourceRequest Create(Guid workspaceId, ResourceType type, long currentLimit, long requestedLimit, string reason, Guid requestedBy)
    {
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type), "Loại tài nguyên không hợp lệ.");
        ArgumentOutOfRangeException.ThrowIfNegative(currentLimit);
        if (requestedLimit <= 0) throw new ArgumentOutOfRangeException(nameof(requestedLimit), "Mức yêu cầu phải lớn hơn 0.");
        if (currentLimit > 0 && requestedLimit <= currentLimit)
            throw new ArgumentOutOfRangeException(nameof(requestedLimit), "Mức yêu cầu phải lớn hơn mức hiện tại.");
        if (currentLimit == 0)
            throw new ArgumentOutOfRangeException(nameof(currentLimit), "Studio đang không giới hạn tài nguyên này.");
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason.Trim().Length > MaxReasonLength)
            throw new ArgumentOutOfRangeException(nameof(reason), $"Lý do tối đa {MaxReasonLength} ký tự.");

        return new ResourceRequest
        {
            WorkspaceId = workspaceId,
            ResourceType = type,
            CurrentLimit = currentLimit,
            RequestedLimit = requestedLimit,
            Reason = reason.Trim(),
            RequestedBy = requestedBy,
            Status = ResourceRequestStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Approve(Guid adminId, string? note)
    {
        EnsurePending();
        Status = ResourceRequestStatus.Approved;
        Review(adminId, note);
    }

    public void Reject(Guid adminId, string note)
    {
        if (string.IsNullOrWhiteSpace(note)) throw new ArgumentException("Từ chối phải ghi lý do.", nameof(note));
        EnsurePending();
        Status = ResourceRequestStatus.Rejected;
        Review(adminId, note);
    }

    private void EnsurePending()
    {
        if (Status != ResourceRequestStatus.Pending)
            throw new InvalidOperationException("Yêu cầu này đã được xử lý.");
    }

    private void Review(Guid adminId, string? note)
    {
        ReviewedBy = adminId;
        ReviewedAt = DateTime.UtcNow;
        ReviewNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, MaxReasonLength)];
        UpdatedAt = DateTime.UtcNow;
    }
}
