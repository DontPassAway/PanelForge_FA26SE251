namespace PanelForge.Domain.Enums;

/// <summary>
/// Vòng đời duyệt cho Series Bible Revision (V2.6 - Q20).
/// 1. Draft (Writer khởi tạo mô tả, đặc tả ngoại hình tính cách, mốc chương).
/// 2. ConceptSubmitted (Artist nhận việc từ Bible Design Queue, vẽ Model Sheet và tải ảnh Asset CAS SHA-256 lên).
/// 3. ReworkRequired (Writer/Editor/Producer yêu cầu sửa lại kèm nhận xét/feedback).
/// 5. CanonLocked (Editor xác nhận và khóa canon; không thể sửa/mở khóa, muốn thay thế dùng /supersede).
/// Lưu ý: Trạng thái WriterEndorsed đã được loại bỏ theo Q20 (Editor xác nhận = khoá Canon).
/// </summary>
public enum BibleRevisionStatus
{
    Draft = 1,
    ConceptSubmitted = 2,
    ReworkRequired = 3,
    CanonLocked = 5
}
