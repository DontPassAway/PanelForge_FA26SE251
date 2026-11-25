using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.UnitTests.Common;

/// <summary>
/// Chương mới mặc định <see cref="ChapterStatus.PreProduction"/> (D12). Test về sản xuất / duyệt / phát hành cần
/// chương đã qua Gate 1 nhưng không quan tâm vòng đời kịch bản, nên đặt thẳng trạng thái chương và giữ kịch bản Draft.
/// Test về chính Gate 1 dùng luồng thật <c>SubmitScript → StartProduction</c>.
/// </summary>
public static class ChapterTestState
{
    public static Chapter InProduction(this Chapter chapter)
    {
        typeof(Chapter).GetProperty(nameof(Chapter.Status))!.SetValue(chapter, ChapterStatus.InProduction);
        return chapter;
    }
}
