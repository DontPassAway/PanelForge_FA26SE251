using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Workspaces.Models;

/// <summary>
/// Cấu hình Assign Automation. <c>RowVersion</c> null = Studio chưa lưu lần nào (đang dùng mặc định, mọi quy tắc tắt).
/// </summary>
public record AutomationSettingsDto(
    Guid WorkspaceId,
    bool AutoNextStage,
    AutoAssignMode AssignMode,
    bool AutoRouteReview,
    int LeadHours,
    int? EscalationHours,
    uint? RowVersion)
{
    public static AutomationSettingsDto Default(Guid workspaceId) => new(
        workspaceId, false, AutoAssignMode.KeepPageOwner, false, WorkspaceAutomationSettings.DefaultLeadHours, null, null);

    public static AutomationSettingsDto From(WorkspaceAutomationSettings s) => new(
        s.WorkspaceId, s.AutoNextStage, s.AssignMode, s.AutoRouteReview, s.LeadHours, s.EscalationHours, s.RowVersion);
}

/// <summary>Body của PUT. Gửi lại <c>RowVersion</c> đã đọc từ GET (null nếu GET trả null) để chặn ghi đè (BR-11).</summary>
public record UpdateAutomationSettingsRequest(
    bool AutoNextStage,
    AutoAssignMode AssignMode,
    bool AutoRouteReview,
    int LeadHours,
    int? EscalationHours,
    uint? RowVersion);