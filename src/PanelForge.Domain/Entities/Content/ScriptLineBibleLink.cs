using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Content;

/// <summary>
/// UC-06: dòng script nhắc tới một mục Series Bible (nhân vật, địa điểm, đạo cụ, thuật ngữ...), ngoài speaker.
/// Nền cho kiểm tra nhất quán bằng code ("đạo cụ X xuất hiện ở dòng Y", consistency-design §6.1).
/// Bỏ liên kết = xoá mềm (giữ lịch sử); mỗi cặp (dòng, mục) chỉ có một liên kết đang hoạt động.
/// </summary>
public class ScriptLineBibleLink : BaseEntity
{
    public const int MaxPerLine = 20;

    public Guid ScriptLineId { get; private set; }
    public Guid BibleEntryId { get; private set; }

    private ScriptLineBibleLink() { }

    public static ScriptLineBibleLink Create(Guid scriptLineId, Guid bibleEntryId) =>
        new() { ScriptLineId = scriptLineId, BibleEntryId = bibleEntryId };
}
