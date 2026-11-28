namespace PanelForge.Domain.Enums;

/// <summary>Trạng thái yêu cầu tăng hạn mức (OPS-21). Pending → Approved | Rejected, không quay lại.</summary>
public enum QuotaIncreaseRequestStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3
}
