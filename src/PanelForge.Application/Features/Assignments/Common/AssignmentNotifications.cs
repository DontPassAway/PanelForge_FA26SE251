using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Common;

/// <summary>Nội dung thông báo cho việc giao/đổi việc (UC-14, BR-14). Handler thêm vào DbContext cùng lần Save với Assignment.</summary>
internal static class AssignmentNotifications
{
    public static Notification Assigned(Assignment a) => Notification.Create(
        a.AssigneeUserId, NotificationType.AssignmentAssigned, "Bạn được giao việc mới", Describe(a), a.Id, a.ChapterId);

    public static Notification Unassigned(Assignment a, Guid previousAssigneeId) => Notification.Create(
        previousAssigneeId, NotificationType.AssignmentUnassigned, "Việc đã được giao cho người khác", Describe(a), a.Id, a.ChapterId);

    public static Notification Updated(Assignment a) => Notification.Create(
        a.AssigneeUserId, NotificationType.AssignmentUpdated, "Việc của bạn vừa được cập nhật", Describe(a), a.Id, a.ChapterId);

    public static Notification Submitted(Assignment a, Guid reviewerUserId) => Notification.Create(
        reviewerUserId, NotificationType.AssignmentSubmitted, "Có việc mới được nộp chờ duyệt", Describe(a), a.Id, a.ChapterId);

    public static Notification Approved(Assignment a) => Notification.Create(
        a.AssigneeUserId, NotificationType.AssignmentApproved, "Việc của bạn đã được duyệt", Describe(a), a.Id, a.ChapterId);

    public static Notification ChangesRequested(Assignment a) => Notification.Create(
        a.AssigneeUserId, NotificationType.AssignmentChangesRequested, "Việc của bạn bị trả về", a.LastFeedbackComment ?? Describe(a), a.Id, a.ChapterId);

    public static Notification ChangeRequestResolved(ChangeRequest cr) => Notification.Create(
        cr.CreatedByUserId, NotificationType.ChangeRequestResolved, "Yêu cầu sửa đã được báo xong, chờ xác minh",
        cr.ResolutionNote ?? cr.Description, cr.AssignmentId, cr.ChapterId);

    public static Notification ChangeRequestReopened(ChangeRequest cr) => Notification.Create(
        cr.ResponsibleUserId, NotificationType.ChangeRequestReopened, "Yêu cầu sửa bị mở lại, chưa đạt",
        cr.ReviewerComment ?? cr.Description, cr.AssignmentId, cr.ChapterId);

    private static string Describe(Assignment a)
    {
        var brief = a.Brief.Length > 200 ? a.Brief[..200] + "…" : a.Brief;
        return a.DueDate is { } due ? $"{brief} (hạn {due:yyyy-MM-dd HH:mm} UTC)" : brief;
    }
}
