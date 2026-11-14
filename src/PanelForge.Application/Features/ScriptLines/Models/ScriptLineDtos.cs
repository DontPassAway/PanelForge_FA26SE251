using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.ScriptLines.Models;

/// <param name="ChapterVersion">VersionVector của Chapter SAU thay đổi; dùng làm <c>expectedChapterVersion</c> cho lần sửa kế tiếp.</param>
/// <param name="RevisionId">Revision Script mới ghi cho thay đổi này (lịch sử, BR-01).</param>
public sealed record ScriptLineDto(
    Guid Id,
    Guid SceneId,
    int LineOrder,
    [property: Obsolete("Removed in BIB-17. Use SpeakerLabel instead.")]
    Guid? SpeakerCharacterId,
    string? DialogueText,
    string? StageDirection,
    Guid? PanelId,
    long ChapterVersion,
    Guid RevisionId,
    IReadOnlyList<Guid> BibleEntryIds,
    NarrationMode NarrationMode = NarrationMode.Present,
    ScriptLineKind Kind = ScriptLineKind.Action,
    string? SpeakerLabel = null,
    string? Text = null)
{
    /// <summary>true = dòng với clientLineId này đã có từ lần gửi trước (gửi lại sau mất mạng), không tạo mới.</summary>
    public bool Replayed { get; init; }

    internal static ScriptLineDto From(ScriptLine l, long chapterVersion, Guid revisionId, IReadOnlyList<Guid> bibleEntryIds) =>
        new(l.Id, l.SceneId, l.LineOrder, null, l.DialogueText, l.StageDirection, l.PanelId, chapterVersion, revisionId,
            bibleEntryIds, l.NarrationMode, l.Kind, l.SpeakerLabel, l.Text);
}

/// <param name="LineOrder">Vị trí chèn (từ 1). Bỏ trống = thêm vào cuối; trùng thì các dòng từ vị trí đó dịch xuống một.</param>
/// <param name="ExpectedChapterVersion">VersionVector chương lúc mở form; lệch thì 409. Bỏ trống = không kiểm.</param>
public sealed record CreateScriptLineRequest(
    string? DialogueText,
    string? StageDirection,
    int? LineOrder,
    long? ExpectedChapterVersion,
    Guid? ClientLineId = null,
    NarrationMode? NarrationMode = null,
    ScriptLineKind? Kind = null,
    string? SpeakerLabel = null,
    string? Text = null);

/// <summary>
/// PUT thay toàn bộ nội dung dòng: field không gửi hoặc null sẽ XOÁ giá trị đó (phải còn thoại hoặc chỉ dẫn).
/// </summary>
public sealed record UpdateScriptLineRequest(
    string? DialogueText,
    string? StageDirection,
    long? ExpectedChapterVersion,
    NarrationMode? NarrationMode = null,
    ScriptLineKind? Kind = null,
    string? SpeakerLabel = null,
    string? Text = null);

public sealed record DeleteScriptLineResult(long ChapterVersion, Guid RevisionId);

/// <summary>Tạo nhanh mục nháp Series Bible từ bôi đen trong dòng kịch bản (BIB-12, 1A, 2A).</summary>
public sealed record NewDraftBibleEntryInput(
    string Name,
    BibleEntryCategory Category = BibleEntryCategory.Character
);

/// <summary>Tham chiếu Series Bible từ bôi đen trên dòng kịch bản (BIB-12, 1A, 2A).</summary>
public sealed record ScriptLineRefInput(
    string SurfaceText,
    Guid? BibleEntryId = null,
    NewDraftBibleEntryInput? NewDraft = null
);

public sealed record ScriptRevisionLineDto(
    Guid LineId, int LineOrder, [property: Obsolete("Removed in BIB-17. Use SpeakerLabel instead.")] Guid? SpeakerCharacterId, string? DialogueText, string? StageDirection);

/// <param name="IsCurrent">true = đây là revision Script mới nhất của Scene (không khôi phục được vì đã là bản hiện tại).</param>
/// <param name="SourceSuggestionId">Đề xuất AI sinh ra revision này; null = sửa tay, baseline hoặc khôi phục.</param>
public sealed record ScriptRevisionDetailDto(
    Guid Id, Guid SceneId, int VersionNumber, string Summary, Guid? PreviousRevisionId, Guid? SourceSuggestionId,
    bool IsCurrent, DateTime CreatedAt, string? CreatedBy, IReadOnlyList<ScriptRevisionLineDto> Lines);

public sealed record RestoreScriptRevisionRequest(long? ExpectedChapterVersion);

/// <param name="RevisionId">Revision MỚI ở đầu lịch sử, mang nội dung của bản được khôi phục (BR-08).</param>
public sealed record RestoreScriptRevisionResult(
    long ChapterVersion, Guid RevisionId, int VersionNumber, Guid RestoredFromRevisionId, int RestoredFromVersionNumber);

public sealed record SceneScriptLineSummaryDto(
    Guid SceneId,
    int TotalLines,
    int DialogueCount,
    int StageDirectionCount,
    IReadOnlyList<Guid> UniqueSpeakerIds
);

public sealed record SceneDetailDto(
    Guid Id,
    Guid ChapterId,
    int SceneNumber,
    string? Heading,
    string? Summary,
    NarrationMode DefaultNarrationMode,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? CreatedBy,
    IReadOnlyList<ScriptLineDto> ScriptLines,
    int Version = 0
);
