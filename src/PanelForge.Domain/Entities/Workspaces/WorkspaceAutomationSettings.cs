using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Workspaces;

/// <summary>
/// Cấu hình Assign Automation của một Studio (một dòng cho mỗi Workspace). Chưa có dòng thì mọi quy tắc coi như tắt.
/// R1 = tự giao công đoạn kế tiếp, R2 = gợi ý người duyệt, R3 = nhắc hạn + leo thang.
/// </summary>
public class WorkspaceAutomationSettings : BaseEntity
{
    public const int MinHours = 1;
    /// <summary>Worker nhắc hạn chỉ xét lô quá hạn trong 7 ngày nên leo thang quá 168 giờ không bao giờ chạy.</summary>
    public const int MaxHours = 168;
    public const int DefaultLeadHours = 24;

    public Guid WorkspaceId { get; private set; }
    public bool AutoNextStage { get; private set; }
    public AutoAssignMode AssignMode { get; private set; } = AutoAssignMode.KeepPageOwner;
    public bool AutoRouteReview { get; private set; }
    public int LeadHours { get; private set; } = DefaultLeadHours;
    /// <summary>Null = tắt leo thang và vòng nhắc theo ô.</summary>
    public int? EscalationHours { get; private set; }

    /// <summary>Concurrency token ánh xạ cột hệ thống <c>xmin</c> (BR-11).</summary>
    public uint RowVersion { get; private set; }

    private WorkspaceAutomationSettings() { }

    public static WorkspaceAutomationSettings Create(Guid workspaceId)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("Thiếu Workspace.", nameof(workspaceId));
        return new WorkspaceAutomationSettings { WorkspaceId = workspaceId, CreatedAt = DateTime.UtcNow };
    }

    public void Update(bool autoNextStage, AutoAssignMode assignMode, bool autoRouteReview, int leadHours, int? escalationHours)
    {
        if (!Enum.IsDefined(assignMode)) throw new ArgumentOutOfRangeException(nameof(assignMode), assignMode, "Chế độ chọn người không hợp lệ.");
        if (leadHours is < MinHours or > MaxHours)
            throw new ArgumentOutOfRangeException(nameof(leadHours), leadHours, $"Giờ nhắc trước hạn phải từ {MinHours} đến {MaxHours}.");
        if (escalationHours is < MinHours or > MaxHours)
            throw new ArgumentOutOfRangeException(nameof(escalationHours), escalationHours,
                $"Giờ leo thang phải để trống hoặc từ {MinHours} đến {MaxHours}.");

        AutoNextStage = autoNextStage;
        AssignMode = assignMode;
        AutoRouteReview = autoRouteReview;
        LeadHours = leadHours;
        EscalationHours = escalationHours;
        UpdatedAt = DateTime.UtcNow;
    }
}