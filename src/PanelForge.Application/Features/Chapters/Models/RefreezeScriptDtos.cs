using PanelForge.Application.Features.Chapters.Queries.GetChapterCast;

namespace PanelForge.Application.Features.Chapters.Models;

public sealed record RefreezeHashParityDto(
    bool IsMatched,
    string CurrentDraftHash,
    string? SubmittedHash,
    int? SubmittedVersionNumber,
    int? LatestVersionNumber);

public sealed record RefreezeBibleGateDto(
    bool IsCanonLocked,
    IReadOnlyList<ChapterCastBlockerDto> Blockers);

public sealed record RefreezeAddedPageDto(
    Guid PageId,
    int PageNumber,
    int PanelCount,
    Guid? SuggestedArtistId,
    string? SuggestedArtistName,
    Guid? SuggestedLettererId,
    string? SuggestedLettererName);

public sealed record RefreezeRemovedPageDto(
    Guid PageId,
    int PageNumber,
    bool CanSafelyDelete,
    int ActiveTaskCount,
    bool HasArtwork,
    bool HasBalloons);

public sealed record RefreezeImpactSummaryDto(
    int VisualImpactCount,
    int TextImpactCount);

public sealed record RefreezeVisualImpactDto(
    int PageNumber,
    int PanelNumber,
    Guid? PanelId,
    string ChangeType,
    string Reason,
    IReadOnlyList<string> AffectedStages,
    IReadOnlyList<string> AffectedAssigneeNames);

public sealed record RefreezeTextImpactDto(
    int PageNumber,
    int PanelNumber,
    Guid? LineId,
    Guid? ElementId,
    string Kind,
    string? SpeakerLabel,
    string? OldText,
    string? NewText,
    bool IsStale);

public sealed record RefreezeScriptPreviewDto(
    Guid ChapterId,
    decimal ChapterNumber,
    string? ChapterTitle,
    Guid? PreviousFrozenVersionId,
    int? PreviousFrozenVersionNumber,
    Guid? SubmittedVersionId,
    int? SubmittedVersionNumber,
    RefreezeHashParityDto HashParity,
    RefreezeBibleGateDto BibleGate,
    IReadOnlyList<RefreezeAddedPageDto> AddedPages,
    IReadOnlyList<RefreezeRemovedPageDto> RemovedPages,
    RefreezeImpactSummaryDto ImpactSummary,
    IReadOnlyList<RefreezeVisualImpactDto> VisualImpacts,
    IReadOnlyList<RefreezeTextImpactDto> TextImpacts);

public sealed record NewPageAssignmentRequest(
    int PageNumber,
    Guid ArtistId,
    Guid? LettererId = null);

public sealed record RefreezeScriptRequest(
    string ExpectedScriptHash,
    IReadOnlyList<NewPageAssignmentRequest> NewPageAssignments);

public sealed record RefreezeScriptResultDto(
    Guid ChapterId,
    Guid FrozenScriptVersionId,
    Guid? PreviousFrozenScriptVersionId,
    int AddedPagesCount,
    int CancelledTasksCount,
    int VisualImpactCount,
    int TextImpactCount,
    string Message);
