namespace PanelForge.Domain.Enums;

/// <summary>Một bước trên dòng thời gian duyệt của một phiên bản Bible (không làm tăng VersionNumber).</summary>
public enum BibleRevisionEventType
{
    Created = 1,
    ConceptSubmitted = 2,
    ReworkRequested = 3,
    Locked = 4,
    Superseded = 5,
    /// <summary>Bản nháp v1 (tạo từ stub) được Writer định nghĩa lại nội dung; lưu nội dung mới để lịch sử khớp bản hiện tại.</summary>
    DraftOverwritten = 6,
    /// <summary>Writer xác nhận mềm (non-blocking) bản vẽ Concept đang nộp.</summary>
    Endorsed = 7
}
