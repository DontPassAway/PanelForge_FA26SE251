namespace PanelForge.Domain.Enums;

/// <summary>Loại tài nguyên cấp phát cho Studio (Resource Allocation).</summary>
public enum ResourceType
{
    Storage = 1,
    AiTokens = 2,
    AiImages = 3
}

/// <summary>Trạng thái yêu cầu tăng tài nguyên. Chỉ Pending mới được duyệt/từ chối/huỷ.</summary>
public enum ResourceRequestStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4
}
