using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Bible.Models;

public sealed record BibleEntryRevisionDto(
    Guid Id,
    Guid BibleEntryId,
    int VersionNumber,
    string Summary,
    string SnapshotJson,
    string ContentHash,
    Guid? AuthorId,
    Guid? AssociatedChapterId,
    int EffectiveFromChapterNumber,
    bool IsInitialVersion,
    DateTime CreatedAt,
    bool IsLocked = false,
    DateTime? LockedAt = null,
    Guid? LockedById = null,
    // Giai đoạn và phạm vi hiệu lực [from, to] (to null = tới hết truyện)
    string? StageName = null,
    int? EffectiveToChapterNumber = null,
    // V2.5: Provenance & 2-tier review
    Guid? ArtistUserId = null,
    BibleRevisionStatus ReviewStatus = BibleRevisionStatus.Draft,
    string? ReworkFeedback = null,
    // Ảnh Concept hiện hành của phiên bản (nộp Concept không tạo phiên bản mới) và dòng thời gian duyệt, cũ → mới
    string? ConceptImageUrl = null,
    IReadOnlyList<BibleRevisionEventDto>? Events = null
)
{
    /// <summary>Events chỉ có khi revision được tải kèm Events (Include) hoặc vừa tạo trong request.</summary>
    public static BibleEntryRevisionDto From(BibleEntryRevision r) => new(
        r.Id, r.BibleEntryId, r.VersionNumber, r.Summary, r.SnapshotJson, r.ContentHash, r.AuthorId, r.AssociatedChapterId,
        r.EffectiveFromChapterNumber, r.IsInitialVersion, r.CreatedAt, r.IsLocked, r.LockedAt, r.LockedById, r.StageName,
        r.EffectiveToChapterNumber, r.ArtistUserId, r.ReviewStatus, r.ReworkFeedback, r.ConceptImageUrl,
        r.Events.OrderBy(e => e.CreatedAt).Select(BibleRevisionEventDto.From).ToList());
}

/// <summary>Một bước duyệt: type Created/ConceptSubmitted/ReworkRequested/Endorsed/Locked/Superseded.</summary>
public sealed record BibleRevisionEventDto(
    Guid Id,
    BibleRevisionEventType Type,
    Guid? ActorId,
    string? Comment,
    string? ImageUrl,
    string EventHash,
    DateTime CreatedAt)
{
    public static BibleRevisionEventDto From(BibleRevisionEvent e) =>
        new(e.Id, e.Type, e.ActorId, e.Comment, e.ImageUrl, e.EventHash, e.CreatedAt);
}

public sealed record BibleEntryDto(
    Guid Id,
    Guid SeriesBibleId,
    BibleEntryCategory Category,
    string Code,
    string Name,
    string? Subtitle,
    string Description,
    string DetailsJson,
    string? ReferenceImageUrl,
    BiblePriority Priority,
    bool StrictCheck,
    bool IsActive,
    BibleEntryRevisionDto? EffectiveRevision,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    bool IsStub = false
);

public sealed record SeriesBibleDto(
    Guid Id,
    Guid SeriesId,
    Guid WorkspaceId,
    string Name,
    string? Description,
    int Version,
    IReadOnlyList<BibleEntryDto> Entries
);

public sealed record CreateBibleEntryRequest(
    BibleEntryCategory Category,
    string Code,
    string Name,
    string Description,
    string? Subtitle = null,
    string? DetailsJson = null,
    string? ReferenceImageUrl = null,
    BiblePriority Priority = BiblePriority.Standard,
    bool StrictCheck = true,
    int EffectiveFromChapterNumber = 1,
    string? StageName = null,
    int? EffectiveToChapterNumber = null
)
{
    public string? EffectiveImageUrl => ReferenceImageUrl;
}

public sealed record AddBibleEntryRevisionRequest(
    string Summary,
    string Name,
    string Description,
    string? Subtitle = null,
    string? DetailsJson = null,
    string? ReferenceImageUrl = null,
    BiblePriority Priority = BiblePriority.Standard,
    bool StrictCheck = true,
    int EffectiveFromChapterNumber = 1,
    string? StageName = null,
    int? EffectiveToChapterNumber = null,
    // UC-04: version FE đã tải về để sửa (versionNumber lớn nhất lúc mở form). Khuyến nghị luôn gửi.
    int? ExpectedVersionNumber = null
)
{
    public string EffectiveSummary => Summary;
    public string? EffectiveImageUrl => ReferenceImageUrl;
}

/// <summary>Producer thay thế bản đã khoá: nội dung mới + lý do; chương hiệu lực lấy theo bản bị thay.</summary>
public sealed record SupersedeBibleEntryRevisionRequest(
    string Reason,
    string Name,
    string Description,
    string? Subtitle = null,
    string? DetailsJson = null,
    string? ReferenceImageUrl = null,
    BiblePriority Priority = BiblePriority.Standard,
    bool StrictCheck = true,
    int? ExpectedVersionNumber = null,
    // null = giữ giai đoạn và chương kết thúc của bản bị thay
    string? StageName = null,
    int? EffectiveToChapterNumber = null
)
{
    public string? EffectiveImageUrl => ReferenceImageUrl;
}

public sealed record EndorseBibleRevisionRequest(string? Note = null, string? EndorsementNote = null)
{
    public string? EffectiveNote => !string.IsNullOrWhiteSpace(Note) ? Note.Trim() : (!string.IsNullOrWhiteSpace(EndorsementNote) ? EndorsementNote.Trim() : null);
}

public sealed record RequestReworkBibleRevisionRequest(string? Reason = null, string? ReworkReason = null)
{
    public string EffectiveReason => (!string.IsNullOrWhiteSpace(Reason) ? Reason : ReworkReason)?.Trim() ?? string.Empty;
}

public sealed record SubmitConceptArtRequest(
    string? Summary = null,
    string? Name = null,
    string? Description = null,
    string? ReferenceImageUrl = null,
    string? ImageAssetUrl = null,
    string? Note = null,
    string? Subtitle = null,
    string? DetailsJson = null,
    string? StageName = null,
    int? EffectiveFromChapterNumber = null,
    int? EffectiveToChapterNumber = null,
    int? ExpectedVersionNumber = null,
    // Phiên bản (giai đoạn) nhận Concept; bỏ trống = giai đoạn bắt đầu ở EffectiveFromChapterNumber, hoặc giai đoạn hiện hành.
    Guid? RevisionId = null
)
{
    public string EffectiveSummary => (!string.IsNullOrWhiteSpace(Summary) ? Summary : (!string.IsNullOrWhiteSpace(Note) ? Note : "Nộp bản vẽ Concept Art")).Trim();
    public string EffectiveImageUrl => (!string.IsNullOrWhiteSpace(ReferenceImageUrl) ? ReferenceImageUrl : ImageAssetUrl)?.Trim() ?? string.Empty;
}

public sealed record BibleDesignQueueItemDto(
    Guid EntryId,
    Guid SeriesId,
    string Code,
    string Name,
    BibleEntryCategory Category,
    string Description,
    string? ReferenceImageUrl,
    int EffectiveFromChapterNumber,
    string? StageName,
    BibleRevisionStatus Status,
    int VersionNumber,
    string? ReworkFeedback,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    bool IsStub = false,
    Guid? RevisionId = null
)
{
    public BibleRevisionStatus ReviewStatus => Status;
}

public sealed record ChapterBibleGateItemDto(
    Guid EntryId,
    string Code,
    string Name,
    BibleEntryCategory Category,
    BibleRevisionStatus CurrentStatus,
    bool IsLocked,
    int? EffectiveFromChapter,
    int? EffectiveToChapter,
    string WarningCode,
    string Message
);

public sealed record ChapterBibleGateResultDto(
    bool IsPassed,
    int TotalReferenced,
    int LockedCount,
    int PendingCount,
    IReadOnlyList<ChapterBibleGateItemDto> Items
);
