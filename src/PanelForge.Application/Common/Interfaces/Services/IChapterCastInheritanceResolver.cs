namespace PanelForge.Application.Common.Interfaces.Services;

/// <summary>
/// Tính lại bản kế thừa của Danh sách thực thể chương (BIB-13, Q23, Q24).
/// Dùng chung cho: commit kịch bản (BIB-12), Writer đổi bản (SetChapterCastRevision),
/// Editor khoá bản thay thế (BIB-10).
/// </summary>
public interface IChapterCastInheritanceResolver
{
    /// <summary>
    /// Với mọi chương cùng Series có số chương &gt;= <paramref name="fromChapterNumber"/> và kịch bản chưa <c>Frozen</c>:
    /// các dòng <c>chapter_bible_bindings</c> có <c>chosen_by_writer = false</c> lấy bản của chương gần nhất phía trước
    /// có thực thể đó, đi theo chuỗi thay thế tới bản đã khoá mới nhất.
    /// Dòng <c>chosen_by_writer = true</c> và chương đã <c>Frozen</c> giữ nguyên.
    /// Chỉ thay đổi entity đang được track; KHÔNG gọi SaveChanges — người gọi lưu trong cùng transaction.
    /// </summary>
    Task ResolveInheritedAsync(Guid seriesId, decimal fromChapterNumber, CancellationToken cancellationToken = default);
}
