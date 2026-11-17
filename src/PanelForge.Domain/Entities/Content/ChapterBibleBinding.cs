using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Content;

/// <summary>
/// Bản đồ thực thể chương (BIB-11, Q16, 5A, 7A):
/// Liên kết thực thể Series Bible với Chapter, gắn bản cụ thể (bound_revision_id)
/// có thể do Writer tự chọn (chosen_by_writer = true) hoặc kế thừa từ chương trước.
/// </summary>
public class ChapterBibleBinding
{
    /// <summary>Concurrency token ánh xạ cột hệ thống <c>xmin</c> của PostgreSQL (BR-11): hai lần ghi chồng nhau → 409.</summary>
    public uint RowVersion { get; private set; }

    public Guid ChapterId { get; private set; }
    public Guid BibleEntryId { get; private set; }
    public Guid? BoundRevisionId { get; private set; }
    public ChapterBibleBindingSource Source { get; private set; }
    public bool ChosenByWriter { get; private set; }
    public bool IsCrowdAtBind { get; private set; }
    public Guid? BoundById { get; private set; }
    public DateTime BoundAt { get; private set; }

    public Chapter Chapter { get; private set; } = default!;
    public BibleEntry BibleEntry { get; private set; } = default!;
    public BibleEntryRevision? BoundRevision { get; private set; }
    public User? BoundBy { get; private set; }

    private ChapterBibleBinding() { }

    public static ChapterBibleBinding Create(
        Guid chapterId,
        Guid bibleEntryId,
        Guid? boundRevisionId,
        ChapterBibleBindingSource source,
        bool chosenByWriter = false,
        bool isCrowdAtBind = false,
        Guid? boundById = null)
    {
        return new ChapterBibleBinding
        {
            ChapterId = chapterId,
            BibleEntryId = bibleEntryId,
            BoundRevisionId = boundRevisionId,
            Source = source,
            ChosenByWriter = chosenByWriter,
            IsCrowdAtBind = isCrowdAtBind,
            BoundById = boundById,
            BoundAt = DateTime.UtcNow
        };
    }

    public void UpdateBinding(
        Guid? boundRevisionId,
        ChapterBibleBindingSource source,
        bool chosenByWriter,
        Guid? boundById = null)
    {
        BoundRevisionId = boundRevisionId;
        Source = source;
        ChosenByWriter = chosenByWriter;
        BoundById = boundById;
        BoundAt = DateTime.UtcNow;
    }

    public void SetCrowdAtBind(bool isCrowd)
    {
        IsCrowdAtBind = isCrowd;
    }
}
