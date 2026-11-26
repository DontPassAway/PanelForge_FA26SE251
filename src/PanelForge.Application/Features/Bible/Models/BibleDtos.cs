using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Bible.Models;

public sealed record WriterEndorsementDto(
    bool Endorsed,
    string? EndorsedByName = null,
    DateTime? EndorsedAt = null,
    int? ConceptVersion = null
);

public sealed record BibleEntryRevisionDto(
    Guid Id,
    Guid BibleEntryId,
    int VersionNumber,
    string Summary,
    string SnapshotJson,
    string ContentHash,
    Guid? AuthorId,
    Guid? AssociatedChapterId,
    bool IsInitialVersion,
    DateTime CreatedAt,
    bool IsLocked = false,
    DateTime? LockedAt = null,
    Guid? LockedById = null,
    // Giai đoạn / biến thể
    string? StageName = null,
    // V2.5: Provenance & 2-tier review
    Guid? ArtistUserId = null,
    BibleRevisionStatus ReviewStatus = BibleRevisionStatus.Draft,
    string? ReworkFeedback = null,
    // Ảnh Concept hiện hành của phiên bản (nộp Concept không tạo phiên bản mới) và dòng thời gian duyệt, cũ → mới
    string? ConceptImageUrl = null,
    IReadOnlyList<BibleRevisionEventDto>? Events = null,
    Guid? SupersedesRevisionId = null,
    Guid? CurrentConceptEventId = null,
    int? CurrentConceptVersionNumber = null,
    WriterEndorsementDto? WriterEndorsement = null
)
{
    public string VariantLabel => StageName ?? string.Empty;
    /// <summary>Events chỉ có khi revision được tải kèm Events (Include) hoặc vừa tạo trong request.</summary>
    public static BibleEntryRevisionDto From(BibleEntryRevision r, IReadOnlyDictionary<Guid, string>? userNames = null)
    {
        var currentEvt = r.CurrentConceptEventId.HasValue ? r.Events.FirstOrDefault(e => e.Id == r.CurrentConceptEventId.Value) : null;
        var currentConceptVer = currentEvt?.ConceptVersionNumber;
        var isEndorsed = r.IsWriterEndorsedForCurrentConcept;
        var endorserName = isEndorsed && r.WriterEndorsedByUserId.HasValue && userNames != null
            ? userNames.GetValueOrDefault(r.WriterEndorsedByUserId.Value)
            : null;
        var endorsement = new WriterEndorsementDto(
            Endorsed: isEndorsed,
            EndorsedByName: endorserName,
            EndorsedAt: isEndorsed ? r.WriterEndorsedAt : null,
            ConceptVersion: isEndorsed ? r.WriterEndorsedConceptVersion : currentConceptVer);

        return new(
            r.Id, r.BibleEntryId, r.VersionNumber, r.Summary, r.SnapshotJson, r.ContentHash, r.AuthorId, r.AssociatedChapterId,
            r.IsInitialVersion, r.CreatedAt, r.IsLocked, r.LockedAt, r.LockedById, r.StageName,
            r.ArtistUserId, r.ReviewStatus, r.ReworkFeedback, r.ConceptImageUrl,
            r.Events.OrderBy(e => e.CreatedAt).Select(BibleRevisionEventDto.From).ToList(),
            r.SupersedesRevisionId,
            r.CurrentConceptEventId,
            currentConceptVer,
            endorsement);
    }
}

/// <summary>Một bước duyệt: type Created/ConceptSubmitted/ReworkRequested/Locked/Superseded.</summary>
public sealed record BibleRevisionEventDto(
    Guid Id,
    BibleRevisionEventType Type,
    Guid? ActorId,
    string? Comment,
    string? ImageUrl,
    string EventHash,
    DateTime CreatedAt,
    int? ConceptVersionNumber = null,
    string? AssetSha256 = null)
{
    public static BibleRevisionEventDto From(BibleRevisionEvent e) =>
        new(e.Id, e.Type, e.ActorId, e.Comment, e.ImageUrl, e.EventHash, e.CreatedAt, e.ConceptVersionNumber, e.AssetSha256);
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
    bool IsStub = false,
    bool IsCrowd = false,
    int? LatestDraftVersionNumber = null,
    IReadOnlyList<string>? Aliases = null,
    BibleEntryRevisionDto? PendingReviewRevision = null
)
{
    public static BibleEntryDto From(BibleEntry entry, IReadOnlyDictionary<Guid, string>? userNames = null)
    {
        var rev = entry.GetDisplayRevision();
        var snapshot = BibleEntrySnapshot.From(entry, rev);
        var normRevStage = rev != null ? BibleNameNormalizer.Normalize(rev.StageName) : "";
        var latestDraftVersionNumber = rev is { ReviewStatus: BibleRevisionStatus.CanonLocked }
            ? entry.Revisions
                .Where(r => string.Equals(BibleNameNormalizer.Normalize(r.StageName), normRevStage, StringComparison.OrdinalIgnoreCase)
                    && r.VersionNumber > rev.VersionNumber
                    && r.ReviewStatus != BibleRevisionStatus.CanonLocked
                    && !entry.IsRevisionSuperseded(r))
                .Select(r => (int?)r.VersionNumber)
                .Max()
            : null;
        var pendingReview = entry.Revisions
            .Where(r => !r.IsLocked && (r.ReviewStatus == BibleRevisionStatus.ConceptSubmitted || r.ReviewStatus == BibleRevisionStatus.ReworkRequired))
            .OrderByDescending(r => r.VersionNumber)
            .FirstOrDefault();

        return new(
            entry.Id,
            entry.SeriesBibleId,
            entry.Category,
            entry.Code,
            entry.Name,
            snapshot.Subtitle,
            snapshot.Description,
            snapshot.DetailsJson,
            snapshot.ReferenceImageUrl,
            snapshot.Priority,
            snapshot.StrictCheck,
            entry.IsActive,
            rev != null ? BibleEntryRevisionDto.From(rev, userNames) : null,
            entry.CreatedAt,
            entry.UpdatedAt,
            entry.IsStub,
            entry.IsCrowd,
            latestDraftVersionNumber,
            entry.Aliases.ToList(),
            pendingReview != null ? BibleEntryRevisionDto.From(pendingReview, userNames) : null
        );
    }
}

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
    string? StageName = null,
    bool IsCrowd = false,
    string? VariantLabel = null,
    IReadOnlyList<string>? Aliases = null
)
{
    public string? EffectiveImageUrl => ReferenceImageUrl;
    public string? EffectiveStageName => !string.IsNullOrWhiteSpace(VariantLabel) ? VariantLabel.Trim() : StageName?.Trim();
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
    string? StageName = null,
    // UC-04: version FE đã tải về để sửa (versionNumber lớn nhất lúc mở form). Khuyến nghị luôn gửi.
    int? ExpectedVersionNumber = null,
    bool IsNewVariant = false,
    Guid? TargetRevisionId = null,
    string? VariantLabel = null
)
{
    public string EffectiveSummary => Summary;
    public string? EffectiveImageUrl => ReferenceImageUrl;
    public string? EffectiveStageName => !string.IsNullOrWhiteSpace(VariantLabel) ? VariantLabel.Trim() : StageName?.Trim();
}

/// <summary>Producer thay thế bản đã khoá: nội dung mới + lý do.</summary>
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
    string? StageName = null,
    Guid? ChapterId = null,
    string? VariantLabel = null
)
{
    public string? EffectiveImageUrl => ReferenceImageUrl;
    public string? EffectiveStageName => !string.IsNullOrWhiteSpace(VariantLabel) ? VariantLabel.Trim() : StageName?.Trim();
}

public sealed record CreateBibleVariantRequest(
    string VariantLabel,
    string Summary,
    string? Name = null,
    string? Description = null,
    string? Subtitle = null,
    string? DetailsJson = null,
    string? ReferenceImageUrl = null,
    BiblePriority Priority = BiblePriority.Standard,
    bool StrictCheck = true
);

public sealed record RequestReworkBibleRevisionRequest(string? Reason = null, string? ReworkReason = null)
{
    public string EffectiveReason => (!string.IsNullOrWhiteSpace(Reason) ? Reason : ReworkReason)?.Trim() ?? string.Empty;
}

public sealed record ConfirmBibleRevisionRequest(Guid? ConceptEventId = null);

public sealed record EndorseBibleRevisionRequest(bool? Endorse = null);

public sealed record UpdateBibleEntryAliasesRequest(IReadOnlyList<string> Aliases);

public sealed record BibleConceptCommentDto(
    Guid Id,
    Guid RevisionId,
    Guid ConceptEventId,
    Guid? ParentCommentId,
    Guid AuthorId,
    string Body,
    string? Region,
    DateTime? ResolvedAt,
    Guid? ResolvedBy,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<BibleConceptCommentDto> Replies
)
{
    public static BibleConceptCommentDto From(BibleConceptComment c) => new(
        c.Id,
        c.RevisionId,
        c.ConceptEventId,
        c.ParentCommentId,
        c.AuthorId,
        c.Body,
        c.Region,
        c.ResolvedAt,
        c.ResolvedBy,
        c.CreatedAt,
        c.UpdatedAt,
        c.Replies.Where(r => !r.IsDeleted).OrderBy(r => r.CreatedAt).Select(From).ToList()
    );
}

public sealed record CreateConceptCommentRequest(
    string Body,
    Guid? ConceptEventId = null,
    string? Region = null
);

public sealed record ReplyConceptCommentRequest(
    string Body
);

public sealed record SubmitConceptArtRequest(
    string? Summary = null,
    string? Name = null,
    string? Description = null,
    string? ReferenceImageUrl = null,
    string? ImageAssetUrl = null,
    string? AssetSha256 = null,
    string? Note = null,
    string? Subtitle = null,
    string? DetailsJson = null,
    string? StageName = null,
    int? ExpectedVersionNumber = null,
    Guid? RevisionId = null,
    string? VariantLabel = null
)
{
    public string EffectiveSummary => (!string.IsNullOrWhiteSpace(Summary) ? Summary : (!string.IsNullOrWhiteSpace(Note) ? Note : "Nộp bản vẽ Concept Art")).Trim();
    public string EffectiveImageUrl => (!string.IsNullOrWhiteSpace(ReferenceImageUrl) ? ReferenceImageUrl : ImageAssetUrl)?.Trim() ?? string.Empty;
    public string? EffectiveStageName => !string.IsNullOrWhiteSpace(VariantLabel) ? VariantLabel.Trim() : StageName?.Trim();
}

public sealed record BibleDesignQueueItemDto(
    Guid EntryId,
    Guid SeriesId,
    string Code,
    string Name,
    BibleEntryCategory Category,
    string Description,
    string? ReferenceImageUrl,
    string? StageName,
    BibleRevisionStatus Status,
    int VersionNumber,
    string? ReworkFeedback,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    bool IsStub = false,
    Guid? RevisionId = null,
    IReadOnlyList<decimal>? WaitingChapterNumbers = null,
    bool IsPriorityRework = false
)
{
    public BibleRevisionStatus ReviewStatus => Status;
    public string VariantLabel => StageName ?? string.Empty;
}

/// <summary>Một chương mà mục Bible xuất hiện trong kịch bản.</summary>
public sealed record BibleEntryUsageDto(
    Guid ChapterId,
    int ChapterNumber,
    string? ChapterTitle,
    int LineCount,
    bool IsScriptFrozen,
    bool IsCanonSatisfied
);
