using PanelForge.Domain.Enums;

namespace PanelForge.Application.Common.Interfaces.Services;

/// <summary>Ảnh một trang đã duyệt, theo thứ tự trang.</summary>
public sealed record ExportPageImage(int PageNumber, byte[] Content, string ContentType);

/// <summary>Thông tin chương ghi vào file (ComicInfo, metadata PDF) và cấu hình preset (JSON).</summary>
public sealed record ExportManifest(
    string SeriesTitle, decimal ChapterNumber, string? ChapterTitle, ReadingDirection ReadingDirection, string ConfigOptionsJson);

public sealed record ExportFile(byte[] Content, string ContentType, string Extension);

/// <summary>
/// Dựng file xuất chương (CBZ, ảnh trang, PDF, webtoon) từ ảnh trang. Là port vì cần thư viện ảnh/PDF ở hạ tầng.
/// Lỗi dữ liệu (ảnh hỏng, chương quá dài…) ném <see cref="InvalidOperationException"/> kèm thông báo cho người dùng.
/// </summary>
public interface IChapterExportRenderer
{
    bool Supports(string format);

    ExportFile Render(string format, ExportManifest manifest, IReadOnlyList<ExportPageImage> pages);
}
