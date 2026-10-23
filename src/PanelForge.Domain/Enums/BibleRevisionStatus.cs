namespace PanelForge.Domain.Enums;

/// <summary>
/// Vòng đời duyệt 2 tầng cho Series Bible Revision (V2.5).
/// 1. Draft (Writer khởi tạo mô tả, đặc tả ngoại hình tính cách, mốc chương).
/// 2. ConceptSubmitted (Artist nhận việc từ Bible Design Queue, vẽ Model Sheet và tải ảnh Asset CAS SHA-256 lên).
/// 3. ReworkRequired (Writer/Editor yêu cầu sửa lại kèm nhận xét/feedback).
/// 4. WriterEndorsed (Writer/Editor xác nhận thiết kế chuẩn với cốt truyện).
/// 5. CanonLocked (Producer duyệt và khóa canon; không thể sửa/mở khóa, muốn thay thế dùng /supersede).
/// </summary>
public enum BibleRevisionStatus
{
    Draft = 1,
    ConceptSubmitted = 2,
    ReworkRequired = 3,
    WriterEndorsed = 4,
    CanonLocked = 5
}
