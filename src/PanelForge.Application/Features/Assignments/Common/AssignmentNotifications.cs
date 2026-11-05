using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Common;

/// <summary>Nội dung thông báo cho việc giao/đổi việc (UC-14, BR-14). Handler thêm vào DbContext cùng lần Save với Assignment.</summary>
internal static class AssignmentNotifications
{
    public static Notification Assigned(Assignment a) => Notification.Create(
        a.AssigneeUserId, NotificationType.AssignmentAssigned, "Bạn được giao việc mới", Describe(a), a.Id, a.ChapterId);

    /// <summary>BR-14 cho giao hàng loạt: một thông báo cho cả đợt, trỏ vào việc đầu tiên của đợt.</summary>
    public static Notification AssignedBatch(IReadOnlyList<Assignment> batch, string stageName)
    {
        var first = batch[0];
        var scope = batch.Count == 1 && first.EntityType == WorkflowEntityType.Chapter ? "cả chương" : $"{batch.Count} trang";
        return Notification.Create(
            first.AssigneeUserId, NotificationType.AssignmentAssigned, $"Bạn được giao {scope} – {stageName}", Describe(first), first.Id, first.ChapterId);
    }

    /// <summary>BR-14 cho giao theo kế hoạch: mỗi người nhận một thông báo gộp mọi stage của họ trong kế hoạch.</summary>
    public static Notification AssignedPlan(IReadOnlyList<Assignment> mine, IReadOnlyDictionary<Guid, string> stageNames)
    {
        var first = mine[0];
        var parts = mine.GroupBy(a => a.PipelineStageId).Select(g =>
        {
            var scope = g.Count() == 1 && g.First().EntityType == WorkflowEntityType.Chapter ? "cả chương" : $"{g.Count()} trang";
            return $"{scope} – {stageNames.GetValueOrDefault(g.Key, "?")}";
        });
        return Notification.Create(
            first.AssigneeUserId, NotificationType.AssignmentAssigned, $"Bạn được giao {string.Join("; ", parts)}",
            Describe(first), first.Id, first.ChapterId);
    }

    /// <summary>Việc tiền đề đã duyệt hết: một thông báo cho mỗi người, trỏ vào việc đầu tiên vừa sẵn sàng.</summary>
    public static Notification ReadyToStart(IReadOnlyList<Assignment> ready)
    {
        var first = ready[0];
        var title = ready.Count == 1 ? "Đến lượt bạn: việc trước đã được duyệt" : $"Đến lượt bạn: {ready.Count} việc đã sẵn sàng";
        return Notification.Create(first.AssigneeUserId, NotificationType.AssignmentReady, title, Describe(first), first.Id, first.ChapterId);
    }

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

    public static Notification DueSoon(Assignment a) => Notification.Create(
        a.AssigneeUserId, NotificationType.AssignmentDueSoon, "Việc sắp đến hạn", Describe(a), a.Id, a.ChapterId);

    public static Notification Overdue(Assignment a) => Notification.Create(
        a.AssigneeUserId, NotificationType.AssignmentOverdue, "Việc đã quá hạn", Describe(a), a.Id, a.ChapterId);

    public static Notification ChangeRequestDueSoon(ChangeRequest cr) => Notification.Create(
        cr.ResponsibleUserId, NotificationType.ChangeRequestDueSoon, "Yêu cầu sửa sắp đến hạn", DescribeChangeRequest(cr), cr.AssignmentId, cr.ChapterId);

    public static Notification ChangeRequestOverdue(ChangeRequest cr) => Notification.Create(
        cr.ResponsibleUserId, NotificationType.ChangeRequestOverdue, "Yêu cầu sửa đã quá hạn", DescribeChangeRequest(cr), cr.AssignmentId, cr.ChapterId);

    public static string Describe(Assignment a)
    {
        var brief = a.Brief.Length > 200 ? a.Brief[..200] + "…" : a.Brief;
        return a.DueDate is { } due ? $"{brief} (hạn {due:yyyy-MM-dd HH:mm} UTC)" : brief;
    }

    private static string DescribeChangeRequest(ChangeRequest cr)
    {
        var brief = cr.Description.Length > 200 ? cr.Description[..200] + "…" : cr.Description;
        return cr.DueDate is { } due ? $"{brief} (hạn {due:yyyy-MM-dd HH:mm} UTC)" : brief;
    }
}
