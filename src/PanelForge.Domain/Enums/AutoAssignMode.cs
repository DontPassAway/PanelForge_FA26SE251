namespace PanelForge.Domain.Enums;

/// <summary>Cách R1 (Assign Automation) chọn người cho công đoạn kế tiếp.</summary>
public enum AutoAssignMode
{
    /// <summary>Giữ người đã làm trang đó ở công đoạn trước; không có thì cân tải.</summary>
    KeepPageOwner,
    /// <summary>Chỉ cân tải: người đúng vai trò đang ít việc nhất.</summary>
    BalanceOnly
}
