using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.ScriptLines.Models;

/// <param name="ChapterVersion">VersionVector của Chapter SAU thay đổi; dùng làm <c>expectedChapterVersion</c> cho lần sửa kế tiếp.</param>
/// <param name="RevisionId">Revision Script mới ghi cho thay đổi này (lịch sử, BR-01).</param>
public sealed record ScriptLineDto(
    Guid Id, Guid SceneId, int LineOrder, Guid? SpeakerCharacterId, string? DialogueText, string? StageDirection,
    Guid? PanelId, long ChapterVersion, Guid RevisionId, IReadOnlyList<Guid> BibleEntryIds,
    NarrationMode NarrationMode = NarrationMode.Present)
{
    /// <summary>true = dòng với clientLineId này đã có từ lần gửi trước (gửi lại sau mất mạng), không tạo mới.</summary>
    public bool Replayed { get; init; }

    internal static ScriptLineDto From(ScriptLine l, long chapterVersion, Guid revisionId, IReadOnlyList<Guid> bibleEntryIds) =>
        new(l.Id, l.SceneId, l.LineOrder, l.SpeakerCharacterId, l.DialogueText, l.StageDirection, l.PanelId, chapterVersion, revisionId,
            bibleEntryIds, l.NarrationMode);
}

/// <param name="LineOrder">Vị trí chèn (từ 1). Bỏ trống = thêm vào cuối; trùng thì các dòng từ vị trí đó dịch xuống một.</param>
/// <param name="ExpectedChapterVersion">VersionVector chương lúc mở form; lệch thì 409. Bỏ trống = không kiểm.</param>
/// <param name="BibleEntryIds">Mục Series Bible mà dòng nhắc tới (địa điểm, đạo cụ, nhân vật...), ngoài speaker.</param>
public sealed record CreateScriptLineRequest(
    string? DialogueText, string? StageDirection, Guid? SpeakerCharacterId, int? LineOrder, long? ExpectedChapterVersion,
    IReadOnlyList<Guid>? BibleEntryIds = null,
    Guid? ClientLineId = null,
    NarrationMode? NarrationMode = null);

/// <summary>
/// PUT thay toàn bộ nội dung dòng: field không gửi hoặc null sẽ XOÁ giá trị đó (phải còn thoại hoặc chỉ dẫn).
/// Ngoại lệ: <c>bibleEntryIds</c> null/không gửi = GIỮ liên kết hiện có; [] = gỡ hết.
/// </summary>
public sealed record UpdateScriptLineRequest(
    string? DialogueText, string? StageDirection, Guid? SpeakerCharacterId, long? ExpectedChapterVersion,
    IReadOnlyList<Guid>? BibleEntryIds = null,
    NarrationMode? NarrationMode = null);

public sealed record DeleteScriptLineResult(long ChapterVersion, Guid RevisionId);

/// <summary>V2.5 & Module A: DTO chuyển giao cho các @-mention trong dòng kịch bản.</summary>
public sealed record ScriptMentionItemDto(
    Guid? BibleEntryId,
    string? RawText,
    BibleEntryCategory? CategoryHint = BibleEntryCategory.Character,
    Guid? ResolvedRevisionId = null
);

/// <summary>V2.5 & Module A: DTO chi tiết liên kết giữa ScriptLine và Series Bible.</summary>
public sealed record ScriptLineBibleLinkDto(
    Guid Id,
    Guid ScriptLineId,
    Guid BibleEntryId,
    string? RawMentionText,
    Guid? ResolvedRevisionId,
    string EntryName,
    BibleEntryCategory Category,
    bool IsStub,
    string? StageName,
    string? ReferenceImageUrl,
    bool IsCanonLocked,
    DateTime CreatedAt
);

public sealed record LinkBibleEntriesRequest(
    IReadOnlyList<ScriptMentionItemDto> Mentions
);

public sealed record ScriptRevisionLineDto(
    Guid LineId, int LineOrder, Guid? SpeakerCharacterId, string? DialogueText, string? StageDirection);

/// <param name="IsCurrent">true = đây là revision Script mới nhất của Scene (không khôi phục được vì đã là bản hiện tại).</param>
/// <param name="SourceSuggestionId">Đề xuất AI sinh ra revision này; null = sửa tay, baseline hoặc khôi phục.</param>
public sealed record ScriptRevisionDetailDto(
    Guid Id, Guid SceneId, int VersionNumber, string Summary, Guid? PreviousRevisionId, Guid? SourceSuggestionId,
    bool IsCurrent, DateTime CreatedAt, string? CreatedBy, IReadOnlyList<ScriptRevisionLineDto> Lines);

public sealed record RestoreScriptRevisionRequest(long? ExpectedChapterVersion);

/// <param name="RevisionId">Revision MỚI ở đầu lịch sử, mang nội dung của bản được khôi phục (BR-08).</param>
public sealed record RestoreScriptRevisionResult(
    long ChapterVersion, Guid RevisionId, int VersionNumber, Guid RestoredFromRevisionId, int RestoredFromVersionNumber);
