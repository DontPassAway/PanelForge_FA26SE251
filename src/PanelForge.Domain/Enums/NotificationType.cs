namespace PanelForge.Domain.Enums;

/// <summary>Loại thông báo trong ứng dụng (UC-14). Nhắc trước hạn sẽ thêm khi có job chạy nền.</summary>
public enum NotificationType
{
    /// <summary>Người nhận được giao việc (hoặc được giao lại). Điều kiện để việc có hiệu lực (BR-14).</summary>
    AssignmentAssigned = 1,
    /// <summary>Việc bị rút khỏi người nhận cũ khi giao lại.</summary>
    AssignmentUnassigned = 2,
    /// <summary>Brief, hạn, ưu tiên hoặc dependency của việc đang giữ bị đổi.</summary>
    AssignmentUpdated = 3,
    /// <summary>Gửi Editor (hoặc Producer nếu Workspace chưa có Editor) khi người nhận nộp việc (CF3 bước 1).</summary>
    AssignmentSubmitted = 4,
    /// <summary>Gửi người nhận khi việc được duyệt cổng.</summary>
    AssignmentApproved = 5,
    /// <summary>Gửi người nhận khi việc bị trả về kèm comment.</summary>
    AssignmentChangesRequested = 6,
    /// <summary>Gửi người tạo yêu cầu sửa khi người chịu trách nhiệm báo đã sửa.</summary>
    ChangeRequestResolved = 7,
    /// <summary>Gửi người chịu trách nhiệm khi Editor mở lại yêu cầu sửa vì chưa đạt.</summary>
    ChangeRequestReopened = 8,
    /// <summary>Gửi Publisher của Workspace khi Producer duyệt chương, chờ duyệt xuất bản.</summary>
    ChapterReadyForPublication = 9,
    /// <summary>Gửi Producer khi chương bị mở lại (Publisher từ chối kèm lý do).</summary>
    ChapterReopened = 10,
    /// <summary>Gửi Producer khi Publisher duyệt và lên lịch phát hành.</summary>
    ChapterPublished = 11,
    /// <summary>Gửi người nhận khi việc chưa xong và hạn còn trong khoảng nhắc (mặc định 24 giờ). BR-14, UC-14.</summary>
    AssignmentDueSoon = 12,
    /// <summary>Gửi người nhận một lần khi việc chưa xong đã quá hạn.</summary>
    AssignmentOverdue = 13,
    /// <summary>Gửi người chịu trách nhiệm khi yêu cầu sửa chưa xong và hạn còn trong khoảng nhắc. UC-14, CF3.</summary>
    ChangeRequestDueSoon = 14,
    /// <summary>Gửi người chịu trách nhiệm một lần khi yêu cầu sửa chưa xong đã quá hạn. UC-14, CF3.</summary>
    ChangeRequestOverdue = 15,
    /// <summary>Gửi người yêu cầu khi bản xuất chương hoàn tất. UC-14, CF5 bước 9.</summary>
    ExportCompleted = 16,
    /// <summary>Gửi người yêu cầu khi bản xuất chương lỗi. UC-14, CF5 bước 9.</summary>
    ExportFailed = 17
}
