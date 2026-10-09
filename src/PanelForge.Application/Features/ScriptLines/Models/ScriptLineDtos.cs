using PanelForge.Domain.Entities.Content;

namespace PanelForge.Application.Features.ScriptLines.Models;

/// <param name="ChapterVersion">VersionVector của Chapter SAU thay đổi; dùng làm <c>expectedChapterVersion</c> cho lần sửa kế tiếp.</param>
/// <param name="RevisionId">Revision Script mới ghi cho thay đổi này (lịch sử, BR-01).</param>
public sealed record ScriptLineDto(
    Guid Id, Guid SceneId, int LineOrder, Guid? SpeakerCharacterId, string? DialogueText, string? StageDirection,
    Guid? PanelId, long ChapterVersion, Guid RevisionId, IReadOnlyList<Guid> BibleEntryIds)
{
    /// <summary>true = dòng với clientLineId này đã có từ lần gửi trước (gửi lại sau mất mạng), không tạo mới.</summary>
    public bool Replayed { get; init; }

    internal static ScriptLineDto From(ScriptLine l, long chapterVersion, Guid revisionId, IReadOnlyList<Guid> bibleEntryIds) =>
        new(l.Id, l.SceneId, l.LineOrder, l.SpeakerCharacterId, l.DialogueText, l.StageDirection, l.PanelId, chapterVersion, revisionId,
            bibleEntryIds);
}

/// <param name="LineOrder">Vị trí chèn (từ 1). Bỏ trống = thêm vào cuối; trùng thì các dòng từ vị trí đó dịch xuống một.</param>
/// <param name="ExpectedChapterVersion">VersionVector chương lúc mở form; lệch thì 409. Bỏ trống = không kiểm.</param>
/// <param name="BibleEntryIds">Mục Series Bible mà dòng nhắc tới (địa điểm, đạo cụ, nhân vật...), ngoài speaker.</param>
public sealed record CreateScriptLineRequest(
    string? DialogueText, string? StageDirection, Guid? SpeakerCharacterId, int? LineOrder, long? ExpectedChapterVersion,
    IReadOnlyList<Guid>? BibleEntryIds = null,
    Guid? ClientLineId = null);  // NFR-09: id client tự sinh (UUID v4) để gửi lại an toàn sau mất mạng

/// <summary>
/// PUT thay toàn bộ nội dung dòng: field không gửi hoặc null sẽ XOÁ giá trị đó (phải còn thoại hoặc chỉ dẫn).
/// Ngoại lệ: <c>bibleEntryIds</c> null/không gửi = GIỮ liên kết hiện có; [] = gỡ hết.
/// </summary>
public sealed record UpdateScriptLineRequest(
    string? DialogueText, string? StageDirection, Guid? SpeakerCharacterId, long? ExpectedChapterVersion,
    IReadOnlyList<Guid>? BibleEntryIds = null);

public sealed record DeleteScriptLineResult(long ChapterVersion, Guid RevisionId);
