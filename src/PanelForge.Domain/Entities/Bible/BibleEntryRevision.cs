using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Bible;

/// <summary>
/// Lưu trữ lịch sử phiên bản bất biến (append-only provenance) của từng mục trong Series Bible.
/// Đảm bảo tính toàn vẹn (Integrity of the Revision Record) và truy vết nguồn gốc (Full Revision Provenance).
/// </summary>
public class BibleEntryRevision : BaseEntity
{
    public Guid BibleEntryId { get; private set; }
    public int VersionNumber { get; private set; }
    public string Summary { get; private set; } = default!;
    public string SnapshotJson { get; private set; } = "{}";
    public Guid? AuthorId { get; private set; }
    public string ContentHash { get; private set; } = default!;
    public Guid? AssociatedChapterId { get; private set; }
    public int EffectiveFromChapterNumber { get; private set; } = 1;

    /// <summary>
    /// Chương cuối cùng revision còn hiệu lực (gồm cả chương này); null = tới hết truyện.
    /// Dùng khi một giai đoạn kết thúc mà không có giai đoạn nối tiếp (vd. nhân vật rời truyện ở chương 50).
    /// Giai đoạn nối tiếp (from lớn hơn) vẫn được ưu tiên trong phần chồng lấn, nên không bắt buộc đặt.
    /// </summary>
    public int? EffectiveToChapterNumber { get; private set; }

    /// <summary>Tên giai đoạn do người dùng đặt, vd. "Lúc nhỏ", "Trưởng thành". Không phải số phiên bản.</summary>
    public string? StageName { get; private set; }

    public bool IsInitialVersion { get; private set; } = false;

    /// <summary>V2.5: Provenance của Artist vẽ bản thiết kế/Model Sheet (P0 1.10 & 1.11).</summary>
    public Guid? ArtistUserId { get; private set; }

    /// <summary>V2.5: Vòng đời kiểm duyệt 2 tầng cho Series Bible.</summary>
    public BibleRevisionStatus ReviewStatus { get; private set; } = BibleRevisionStatus.Draft;

    /// <summary>V2.5: Nhận xét/yêu cầu sửa của Writer hoặc Editor khi chuyển sang trạng thái ReworkRequired.</summary>
    public string? ReworkFeedback { get; private set; }

    /// <summary>
    /// Ảnh Concept hiện hành của phiên bản (ảnh của sự kiện ConceptSubmitted mới nhất). Là trạng thái chiếu từ Events,
    /// không thuộc nội dung đã băm: nộp/nộp lại Concept không đổi SnapshotJson/ContentHash và không tăng VersionNumber.
    /// </summary>
    public string? ConceptImageUrl { get; private set; }

    /// <summary>Dòng thời gian duyệt append-only của phiên bản này.</summary>
    public ICollection<BibleRevisionEvent> Events { get; private set; } = [];

    /// <summary>Đã qua ít nhất một bước kiểm duyệt (ngoài sự kiện Created): xoá đi sẽ mất provenance.</summary>
    public bool HasReviewHistory => Events.Any(e => e.Type != BibleRevisionEventType.Created);

    public bool CoversChapter(int chapterNumber)
        => EffectiveFromChapterNumber <= chapterNumber
           && (EffectiveToChapterNumber is null || chapterNumber <= EffectiveToChapterNumber);

    /// <summary>V1.2: Producer đã duyệt và khoá revision này làm tham chiếu. Chỉ thêm metadata, nội dung (snapshot, hash) vẫn bất biến; không có thao tác mở khoá.</summary>
    public DateTime? LockedAt { get; private set; }
    public Guid? LockedById { get; private set; }
    public bool IsLocked => LockedAt.HasValue || ReviewStatus == BibleRevisionStatus.CanonLocked;

    public BibleEntry BibleEntry { get; private set; } = default!;

    private BibleEntryRevision() { }

    public void Lock(Guid userId)
    {
        if (IsLocked) throw new InvalidOperationException("Revision đã được khoá.");
        LockedAt = DateTime.UtcNow;
        LockedById = userId;
        ReviewStatus = BibleRevisionStatus.CanonLocked;
        // Chốt luôn ảnh Concept lúc khoá vào sự kiện, để canon không phụ thuộc trạng thái chiếu về sau
        Events.Add(BibleRevisionEvent.Create(Id, BibleRevisionEventType.Locked, userId, imageUrl: ConceptImageUrl));
    }

    /// <summary>
    /// Artist nộp (hoặc nộp lại sau khi bị yêu cầu sửa) bản vẽ Concept cho chính phiên bản này: chỉ thêm sự kiện, không tạo
    /// phiên bản mới. Đã khoá thì phải Supersede. Nộp lại sau khi đã duyệt sẽ phải duyệt lại.
    /// </summary>
    public void SubmitConcept(Guid artistId, string imageUrl, string? note)
    {
        if (IsLocked) throw new InvalidOperationException("Revision đã được khoá Canon; muốn đổi thiết kế phải dùng /supersede.");
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUrl);
        ArtistUserId = artistId;
        ConceptImageUrl = imageUrl.Trim();
        ReviewStatus = BibleRevisionStatus.ConceptSubmitted;
        ReworkFeedback = null;
        Events.Add(BibleRevisionEvent.Create(Id, BibleRevisionEventType.ConceptSubmitted, artistId, note, ConceptImageUrl));
    }

    /// <summary>Bản khoá này vừa bị một phiên bản mới thay thế; chỉ ghi sự kiện, nội dung và khoá giữ nguyên.</summary>
    public void MarkSuperseded(Guid userId, int byVersionNumber, string reason) =>
        Events.Add(BibleRevisionEvent.Create(Id, BibleRevisionEventType.Superseded, userId, $"Bị thay bởi v{byVersionNumber}: {reason}"));

    /// <summary>
    /// Writer/Editor xác nhận thiết kế. Mục có hình (<paramref name="requiresConcept"/>) chỉ duyệt được khi Artist đã nộp Concept;
    /// mục chỉ có văn bản (thuật ngữ, quy tắc, sự kiện) duyệt thẳng từ bản nháp.
    /// </summary>
    public void Endorse(Guid reviewerId, bool requiresConcept = true)
    {
        if (IsLocked) throw new InvalidOperationException("Revision đã được khoá.");
        var allowed = ReviewStatus == BibleRevisionStatus.ConceptSubmitted
            || (!requiresConcept && ReviewStatus == BibleRevisionStatus.Draft);
        if (!allowed)
            throw new InvalidOperationException(ReviewStatus switch
            {
                BibleRevisionStatus.WriterEndorsed => "Phiên bản này đã được duyệt.",
                BibleRevisionStatus.ReworkRequired => "Đang chờ Artist vẽ lại; chỉ duyệt được sau khi Artist nộp lại Concept.",
                _ => "Artist chưa nộp Concept cho phiên bản này."
            });
        ReviewStatus = BibleRevisionStatus.WriterEndorsed;
        ReworkFeedback = null;
        Events.Add(BibleRevisionEvent.Create(Id, BibleRevisionEventType.Endorsed, reviewerId, imageUrl: ConceptImageUrl));
    }

    public void RequestRework(Guid reviewerId, string feedback)
    {
        if (IsLocked) throw new InvalidOperationException("Revision đã được khoá, không thể yêu cầu sửa trực tiếp; phải dùng /supersede.");
        ArgumentException.ThrowIfNullOrWhiteSpace(feedback);
        if (ReviewStatus is not (BibleRevisionStatus.ConceptSubmitted or BibleRevisionStatus.WriterEndorsed))
            throw new InvalidOperationException(ReviewStatus == BibleRevisionStatus.ReworkRequired
                ? "Phiên bản này đang chờ vẽ lại."
                : "Chưa có Concept để yêu cầu vẽ lại; muốn sửa nội dung hãy tạo bản sửa mới.");
        ReviewStatus = BibleRevisionStatus.ReworkRequired;
        ReworkFeedback = feedback.Trim();
        Events.Add(BibleRevisionEvent.Create(Id, BibleRevisionEventType.ReworkRequested, reviewerId, ReworkFeedback, ConceptImageUrl));
    }

    public void AssignArtist(Guid artistId)
    {
        ArtistUserId = artistId;
    }

    public static BibleEntryRevision Create(
        Guid bibleEntryId,
        int versionNumber,
        string summary,
        string snapshotJson,
        string contentHash,
        Guid? authorId = null,
        Guid? associatedChapterId = null,
        int effectiveFromChapterNumber = 1,
        bool isInitialVersion = false,
        string? stageName = null,
        int? effectiveToChapterNumber = null,
        Guid? artistUserId = null,
        BibleRevisionStatus reviewStatus = BibleRevisionStatus.Draft,
        string? reworkFeedback = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(versionNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        var from = Math.Max(1, effectiveFromChapterNumber);
        if (effectiveToChapterNumber < from)
            throw new ArgumentException($"Chương kết thúc hiệu lực ({effectiveToChapterNumber}) phải ≥ chương bắt đầu ({from}).");
        if (stageName?.Trim().Length > 100)
            throw new ArgumentException("Tên giai đoạn tối đa 100 ký tự.");

        var revision = new BibleEntryRevision
        {
            BibleEntryId = bibleEntryId,
            VersionNumber = versionNumber,
            Summary = summary.Trim(),
            SnapshotJson = string.IsNullOrWhiteSpace(snapshotJson) ? "{}" : snapshotJson.Trim(),
            ContentHash = contentHash.Trim(),
            AuthorId = authorId,
            AssociatedChapterId = associatedChapterId,
            EffectiveFromChapterNumber = from,
            EffectiveToChapterNumber = effectiveToChapterNumber,
            StageName = string.IsNullOrWhiteSpace(stageName) ? null : stageName.Trim(),
            IsInitialVersion = isInitialVersion,
            ArtistUserId = artistUserId,
            ReviewStatus = reviewStatus,
            ReworkFeedback = reworkFeedback?.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        revision.Events.Add(BibleRevisionEvent.Create(revision.Id, BibleRevisionEventType.Created, authorId, revision.Summary));
        return revision;
    }
}
