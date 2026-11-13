using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Models;

public sealed record ChapterDto(
    Guid Id,
    Guid SeriesId,
    decimal ChapterNumber,
    string? Title,
    DateOnly? TargetReleaseDate,
    long VersionVector,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? CreatedBy,
    int ScenesCount,
    int PagesCount,
    ChapterStatus Status = ChapterStatus.InProduction,
    DateTime? PublishedAt = null,
    string? Outline = null,
    bool IsScriptFrozen = false,
    DateTime? ScriptFrozenAt = null,
    Guid? ScriptFrozenById = null,
    ScriptReopenStatus ScriptReopenStatus = ScriptReopenStatus.None,
    string? ScriptReopenReason = null,
    Guid? ScriptReopenRequestedById = null,
    DateTime? ScriptReopenRequestedAt = null,
    // Ảnh bìa chương (null = FE dùng badge số tập); có ?v= theo nội dung, tải bằng fetch + Bearer
    string? CoverUrl = null,
    string? ScriptContentHash = null,
    bool IsLocked = false,
    decimal PriceVnd = 0m,
    // D12: vòng đời kịch bản Draft ↔ InReview → Frozen
    ScriptStatus ScriptStatus = ScriptStatus.Draft,
    DateTime? ScriptSubmittedAt = null,
    // Phiên bản đã gửi duyệt gần nhất và lần trước (so sánh từng dòng)
    Guid? LastSubmittedVersionId = null,
    Guid? PreviousSubmittedVersionId = null
);

public sealed record ChapterSceneSummaryDto(
    Guid Id,
    int SceneNumber,
    string? Heading,
    string? Summary
);

public sealed record ChapterPageSummaryDto(
    Guid Id,
    int PageNumber,
    string LayoutFormat,
    int WidthPx,
    int HeightPx,
    int Dpi
);

public sealed record ChapterDetailDto(
    Guid Id,
    Guid SeriesId,
    decimal ChapterNumber,
    string? Title,
    DateOnly? TargetReleaseDate,
    long VersionVector,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? CreatedBy,
    IReadOnlyList<ChapterSceneSummaryDto> Scenes,
    IReadOnlyList<ChapterPageSummaryDto> Pages,
    ChapterStatus Status = ChapterStatus.InProduction,
    DateTime? PublishedAt = null,
    string? Outline = null,
    bool IsScriptFrozen = false,
    DateTime? ScriptFrozenAt = null,
    Guid? ScriptFrozenById = null,
    ScriptReopenStatus ScriptReopenStatus = ScriptReopenStatus.None,
    string? ScriptReopenReason = null,
    Guid? ScriptReopenRequestedById = null,
    DateTime? ScriptReopenRequestedAt = null,
    // Ảnh bìa chương (null = FE dùng badge số tập); có ?v= theo nội dung, tải bằng fetch + Bearer
    string? CoverUrl = null,
    string? ScriptContentHash = null,
    bool IsLocked = false,
    decimal PriceVnd = 0m,
    // D12: vòng đời kịch bản Draft ↔ InReview → Frozen
    ScriptStatus ScriptStatus = ScriptStatus.Draft,
    DateTime? ScriptSubmittedAt = null,
    // Phiên bản đã gửi duyệt gần nhất và lần trước (so sánh từng dòng)
    Guid? LastSubmittedVersionId = null,
    Guid? PreviousSubmittedVersionId = null
);

public sealed record CreateChapterRequest(
    decimal ChapterNumber,
    string? Title = null,
    DateOnly? TargetReleaseDate = null,
    string? Outline = null,
    bool? IsLocked = null,
    decimal? PriceVnd = null
);

public sealed record UpdateChapterRequest(
    string? Title,
    DateOnly? TargetReleaseDate,
    bool IncrementVersion = false,
    string? Outline = null,
    bool? IsLocked = null,
    decimal? PriceVnd = null
);
