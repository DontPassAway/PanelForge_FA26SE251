using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Workspaces;

/// <summary>
/// Producer xin Admin tăng một loại hạn mức của Studio (OPS-21). Mỗi Studio chỉ có một yêu cầu đang chờ cho mỗi loại.
/// Duyệt/từ chối là quyết định một lần: đã xử lý thì không đổi nữa.
/// </summary>
public class QuotaIncreaseRequest : BaseEntity
{
    public const int MaxReasonLength = 1000;
    public const int MaxReviewNoteLength = 1000;

    /// <summary>xmin: hai Admin xử lý cùng lúc → người sau nhận 409.</summary>
    public uint RowVersion { get; private set; }

    public Guid WorkspaceId { get; private set; }
    public QuotaResourceType ResourceType { get; private set; }
    /// <summary>Mức đang cấp lúc gửi yêu cầu.</summary>
    public long CurrentQuota { get; private set; }
    /// <summary>Mức mới Producer xin (giá trị tuyệt đối, không phải phần cộng thêm).</summary>
    public long RequestedQuota { get; private set; }
    public string Reason { get; private set; } = default!;
    public QuotaIncreaseRequestStatus Status { get; private set; }
    public Guid RequestedByUserId { get; private set; }

    public Guid? ReviewedByUserId { get; private set; }
    public DateTime? ReviewedAt { get; private set; }
    public string? ReviewNote { get; private set; }
    /// <summary>Mức Admin thực cấp khi duyệt (có thể khác mức xin).</summary>
    public long? GrantedQuota { get; private set; }

    public StudioWorkspace Workspace { get; private set; } = default!;

    private QuotaIncreaseRequest() { }

    public static QuotaIncreaseRequest Submit(
        Guid workspaceId, QuotaResourceType resourceType, long currentQuota, long requestedQuota, string reason, Guid requestedByUserId)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("Thiếu Studio.", nameof(workspaceId));
        if (requestedByUserId == Guid.Empty) throw new ArgumentException("Thiếu người gửi.", nameof(requestedByUserId));
        if (!Enum.IsDefined(resourceType)) throw new ArgumentException("Loại hạn mức không hợp lệ.", nameof(resourceType));
        if (currentQuota == 0)
            throw new ArgumentException("Hạn mức này đang không giới hạn, không cần xin thêm.", nameof(currentQuota));
        if (requestedQuota <= currentQuota)
            throw new ArgumentException("Mức xin phải lớn hơn mức đang cấp.", nameof(requestedQuota));

        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed)) throw new ArgumentException("Cần ghi lý do xin tăng hạn mức.", nameof(reason));
        if (trimmed.Length > MaxReasonLength)
            throw new ArgumentException($"Lý do tối đa {MaxReasonLength} ký tự.", nameof(reason));

        return new QuotaIncreaseRequest
        {
            WorkspaceId = workspaceId,
            ResourceType = resourceType,
            CurrentQuota = currentQuota,
            RequestedQuota = requestedQuota,
            Reason = trimmed,
            Status = QuotaIncreaseRequestStatus.Pending,
            RequestedByUserId = requestedByUserId
        };
    }

    /// <summary>Duyệt với mức cấp thật (mặc định bằng mức xin). Mức cấp phải dương; muốn bỏ giới hạn thì Admin sửa trực tiếp.</summary>
    public void Approve(Guid adminId, long? grantedQuota, string? note)
    {
        EnsurePending();
        var granted = grantedQuota ?? RequestedQuota;
        if (granted <= 0) throw new ArgumentException("Mức cấp phải lớn hơn 0.", nameof(grantedQuota));

        Status = QuotaIncreaseRequestStatus.Approved;
        GrantedQuota = granted;
        Close(adminId, note);
    }

    public void Reject(Guid adminId, string note)
    {
        EnsurePending();
        if (string.IsNullOrWhiteSpace(note)) throw new ArgumentException("Cần ghi lý do từ chối.", nameof(note));

        Status = QuotaIncreaseRequestStatus.Rejected;
        Close(adminId, note);
    }

    private void Close(Guid adminId, string? note)
    {
        if (adminId == Guid.Empty) throw new ArgumentException("Thiếu người duyệt.", nameof(adminId));
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed is { Length: > MaxReviewNoteLength })
            throw new ArgumentException($"Ghi chú tối đa {MaxReviewNoteLength} ký tự.", nameof(note));

        ReviewedByUserId = adminId;
        ReviewedAt = DateTime.UtcNow;
        ReviewNote = trimmed;
    }

    private void EnsurePending()
    {
        if (Status != QuotaIncreaseRequestStatus.Pending)
            throw new InvalidOperationException("Yêu cầu này đã được xử lý.");
    }
}
