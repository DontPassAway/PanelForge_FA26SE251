using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Identity;

namespace PanelForge.Domain.Entities.Content;

/// <summary>
/// Bản theo Panel (BIB-11, Q23, Q24) — giai đoạn khác trong cùng chương (hồi tưởng):
/// Cho phép một Panel chỉ định bản BibleEntryRevision riêng cho một thực thể,
/// ghi đè bản mặc định của chương tại Panel đó.
/// </summary>
public class PanelBibleVariant
{
    public Guid PanelId { get; private set; }
    public Guid BibleEntryId { get; private set; }
    public Guid RevisionId { get; private set; }
    public Guid SetById { get; private set; }
    public DateTime SetAt { get; private set; }

    public Panel Panel { get; private set; } = default!;
    public BibleEntry BibleEntry { get; private set; } = default!;
    public BibleEntryRevision Revision { get; private set; } = default!;
    public User SetBy { get; private set; } = default!;

    private PanelBibleVariant() { }

    public static PanelBibleVariant Create(
        Guid panelId,
        Guid bibleEntryId,
        Guid revisionId,
        Guid setById)
    {
        return new PanelBibleVariant
        {
            PanelId = panelId,
            BibleEntryId = bibleEntryId,
            RevisionId = revisionId,
            SetById = setById,
            SetAt = DateTime.UtcNow
        };
    }

    public void UpdateRevision(Guid revisionId, Guid setById)
    {
        RevisionId = revisionId;
        SetById = setById;
        SetAt = DateTime.UtcNow;
    }
}
