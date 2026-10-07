namespace PanelForge.Domain.Enums;

/// <summary>Quyết định duyệt cấp Chapter, ghi append-only vào lịch sử phê duyệt (họp tuần 5).</summary>
public enum ChapterReviewAction
{
    /// <summary>Producer duyệt bản hoàn chỉnh, chuyển cho Publisher.</summary>
    ProducerApproved = 1,
    /// <summary>Producer tự mở lại chương đã duyệt để sửa.</summary>
    ProducerReopened = 2,
    /// <summary>Publisher từ chối xuất bản, trả chương về sản xuất kèm lý do.</summary>
    PublisherRejected = 3,
    /// <summary>Publisher duyệt xuất bản và lên lịch (hoặc phát hành ngay).</summary>
    PublisherPublished = 4
}
