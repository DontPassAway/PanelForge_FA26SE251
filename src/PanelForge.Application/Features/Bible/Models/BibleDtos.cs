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
    int? EffectiveToChapterNumber = null
);

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
    DateTime? UpdatedAt
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
);

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
);

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
);
