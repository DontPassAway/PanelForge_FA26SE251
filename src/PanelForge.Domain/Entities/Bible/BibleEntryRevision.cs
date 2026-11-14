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

    /// <summary>V2.5: Vòng đời kiểm duyệt cho Series Bible (V2.6: Draft -> ConceptSubmitted -> CanonLocked, hoặc ReworkRequired).</summary>
    public BibleRevisionStatus ReviewStatus { get; private set; } = BibleRevisionStatus.Draft;

    /// <summary>V2.5: Nhận xét/yêu cầu sửa của Writer, Editor hoặc Producer khi chuyển sang trạng thái ReworkRequired.</summary>
    public string? ReworkFeedback { get; private set; }

    /// <summary>
    /// Ảnh Concept hiện hành của phiên bản (ảnh của sự kiện ConceptSubmitted mới nhất). Là trạng thái chiếu từ Events,
    /// không thuộc nội dung đã băm: nộp/nộp lại Concept không đổi SnapshotJson/ContentHash và không tăng VersionNumber.
    /// </summary>
    public string? ConceptImageUrl { get; private set; }

    /// <summary>V2.6 (Q20): ID của sự kiện ConceptSubmitted đang nộp mới nhất.</summary>
    public Guid? CurrentConceptEventId { get; private set; }
    public BibleRevisionEvent? CurrentConceptEvent { get; private set; }

    /// <summary>Dòng thời gian duyệt append-only của phiên bản này.</summary>
    public ICollection<BibleRevisionEvent> Events { get; private set; } = [];

    /// <summary>Đã qua ít nhất một bước kiểm duyệt (ngoài sự kiện Created): xoá đi sẽ mất provenance.</summary>
    public bool HasReviewHistory => Events.Any(e => e.Type != BibleRevisionEventType.Created);

    public bool CoversChapter(int chapterNumber)
        => EffectiveFromChapterNumber <= chapterNumber
           && (EffectiveToChapterNumber is null || chapterNumber <= EffectiveToChapterNumber);

    /// <summary>Editor đã xác nhận và khoá revision này làm Canon tham chiếu. Chỉ thêm metadata, nội dung vẫn bất biến; không có thao tác mở khoá.</summary>
    public DateTime? LockedAt { get; private set; }
    public Guid? LockedById { get; private set; }
    public bool IsLocked => LockedAt.HasValue || ReviewStatus == BibleRevisionStatus.CanonLocked;
    public bool IsSuperseded => Events.Any(e => e.Type == BibleRevisionEventType.Superseded);

    /// <summary>
    /// BIB-09 & Q23: Revision mà bản này thay thế (cùng biến thể).
    /// Bản cũ vẫn giữ trạng thái CanonLocked cho tới khi bản thay thế được Editor khoá Canon (BIB-10).
    /// </summary>
    public Guid? SupersedesRevisionId { get; private set; }
    public BibleEntryRevision? SupersedesRevision { get; private set; }

    public BibleEntry BibleEntry { get; internal set; } = default!;

    private BibleEntryRevision() { }

    public void Lock(Guid userId)
    {
        if (IsLocked) throw new InvalidOperationException("Revision đã được khoá.");
        LockedAt = DateTime.UtcNow;
        LockedById = userId;
        ReviewStatus = BibleRevisionStatus.CanonLocked;
        var currentEvt = CurrentConceptEventId.HasValue ? Events.FirstOrDefault(e => e.Id == CurrentConceptEventId.Value) : null;
        Events.Add(BibleRevisionEvent.Create(
            Id,
            BibleRevisionEventType.Locked,
            userId,
            imageUrl: ConceptImageUrl,
            conceptVersionNumber: currentEvt?.ConceptVersionNumber,
            assetSha256: currentEvt?.AssetSha256));
    }

    /// <summary>
    /// V2.6 (Q20): Editor xác nhận = khoá Canon. Chỉ từ ConceptSubmitted, conceptEventId phải là phiên bản đang nộp.
    /// Editor phải khác Artist đã nộp phiên bản đó (BR-06).
    /// </summary>
    public void ConfirmCanon(Guid editorId, Guid conceptEventId)
    {
        if (IsLocked) throw new InvalidOperationException("Revision đã được khoá.");
        if (ReviewStatus != BibleRevisionStatus.ConceptSubmitted)
            throw new InvalidOperationException(ReviewStatus switch
            {
                BibleRevisionStatus.ReworkRequired => "Đang chờ Artist vẽ lại; chỉ xác nhận được sau khi Artist nộp lại Concept.",
                _ => "Artist chưa nộp Concept cho phiên bản này."
            });

        if (!CurrentConceptEventId.HasValue || CurrentConceptEventId.Value != conceptEventId)
            throw new InvalidOperationException("Phiên bản concept xác nhận không phải phiên bản đang nộp.");

        var currentEvt = Events.FirstOrDefault(e => e.Id == conceptEventId);
        if ((currentEvt?.ActorId.HasValue == true && currentEvt.ActorId.Value == editorId)
            || (ArtistUserId.HasValue && ArtistUserId.Value == editorId))
        {
            throw new UnauthorizedAccessException("Editor không thể tự xác nhận bản vẽ do chính mình nộp (BR-06).");
        }

        LockedAt = DateTime.UtcNow;
        LockedById = editorId;
        ReviewStatus = BibleRevisionStatus.CanonLocked;
        Events.Add(BibleRevisionEvent.Create(
            Id,
            BibleRevisionEventType.Locked,
            editorId,
            imageUrl: ConceptImageUrl,
            conceptVersionNumber: currentEvt?.ConceptVersionNumber,
            assetSha256: currentEvt?.AssetSha256));
    }

    /// <summary>
    /// Artist nộp bản vẽ Concept (v1, v2, ...). Trạng thái chuyển thành ConceptSubmitted, tăng ConceptVersionNumber.
    /// Cho phép nộp từ Draft, ReworkRequired hoặc ConceptSubmitted (nộp đè phiên bản mới khi chưa duyệt).
    /// </summary>
    public void SubmitConcept(Guid artistId, string imageUrl, string? note, string? assetSha256 = null)
    {
        if (IsLocked) throw new InvalidOperationException("Revision đã được khoá Canon; muốn đổi thiết kế phải dùng /supersede.");
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUrl);
        ArtistUserId = artistId;
        ConceptImageUrl = imageUrl.Trim();
        ReviewStatus = BibleRevisionStatus.ConceptSubmitted;
        ReworkFeedback = null;

        var nextVersion = (Events
            .Where(e => e.Type == BibleRevisionEventType.ConceptSubmitted && e.ConceptVersionNumber.HasValue)
            .Select(e => e.ConceptVersionNumber!.Value)
            .DefaultIfEmpty(0)
            .Max()) + 1;

        var ev = BibleRevisionEvent.Create(
            Id,
            BibleRevisionEventType.ConceptSubmitted,
            artistId,
            note,
            ConceptImageUrl,
            conceptVersionNumber: nextVersion,
            assetSha256: assetSha256);

        CurrentConceptEventId = ev.Id;
        Events.Add(ev);
    }

    /// <summary>Bản khoá này vừa bị một phiên bản mới thay thế; chỉ ghi sự kiện, nội dung và khoá giữ nguyên.</summary>
    public void MarkSuperseded(Guid userId, int byVersionNumber, string reason) =>
        Events.Add(BibleRevisionEvent.Create(Id, BibleRevisionEventType.Superseded, userId, $"Bị thay bởi v{byVersionNumber}: {reason}"));

    public void RequestRework(Guid reviewerId, string feedback)
    {
        if (IsLocked) throw new InvalidOperationException("Revision đã được khoá, không thể yêu cầu sửa trực tiếp; phải dùng /supersede.");
        ArgumentException.ThrowIfNullOrWhiteSpace(feedback);
        if (ReviewStatus != BibleRevisionStatus.ConceptSubmitted)
            throw new InvalidOperationException(ReviewStatus == BibleRevisionStatus.ReworkRequired
                ? "Phiên bản này đang chờ vẽ lại."
                : "Chưa có Concept để yêu cầu vẽ lại; muốn sửa nội dung hãy tạo bản sửa mới.");
        ReviewStatus = BibleRevisionStatus.ReworkRequired;
        ReworkFeedback = feedback.Trim();
        var currentEvt = CurrentConceptEventId.HasValue ? Events.FirstOrDefault(e => e.Id == CurrentConceptEventId.Value) : null;
        Events.Add(BibleRevisionEvent.Create(
            Id,
            BibleRevisionEventType.ReworkRequested,
            reviewerId,
            ReworkFeedback,
            ConceptImageUrl,
            conceptVersionNumber: currentEvt?.ConceptVersionNumber,
            assetSha256: currentEvt?.AssetSha256));
    }

    public void AssignArtist(Guid artistId)
    {
        ArtistUserId = artistId;
    }

    public void SetSupersedesRevisionId(Guid? supersedesRevisionId)
    {
        SupersedesRevisionId = supersedesRevisionId;
    }

    /// <summary>
    /// BIB-13 & 4A: Ghi đè revision v1 Draft khi Writer định nghĩa mục nháp (DefineBibleEntryCommand).
    /// Giữ nguyên versionNumber và ID, chỉ cập nhật nội dung văn bản đặc tả đã hoàn chỉnh.
    /// </summary>
    public void OverwriteDraft(string summary, string snapshotJson, string contentHash, Guid authorId, string? stageName = null)
    {
        if (IsLocked) throw new InvalidOperationException("Không thể ghi đè revision đã được khoá.");
        if (HasReviewHistory) throw new InvalidOperationException("Không thể ghi đè revision đã có lịch sử kiểm duyệt.");
        Summary = summary;
        SnapshotJson = snapshotJson;
        ContentHash = contentHash;
        AuthorId = authorId;
        if (!string.IsNullOrWhiteSpace(stageName)) StageName = stageName.Trim();
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
        string? reworkFeedback = null,
        Guid? supersedesRevisionId = null)
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
            SupersedesRevisionId = supersedesRevisionId,
            CreatedAt = DateTime.UtcNow
        };
        revision.Events.Add(BibleRevisionEvent.Create(revision.Id, BibleRevisionEventType.Created, authorId, revision.Summary));
        return revision;
    }
}
