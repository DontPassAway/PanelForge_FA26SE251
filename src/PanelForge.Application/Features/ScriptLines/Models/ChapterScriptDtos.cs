using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.ScriptLines.Models;

public sealed record ScriptTreeCast(Guid EntryId, Guid RevisionId);

/// <param name="Id">Id client cho dòng mới (giữ nguyên làm Id dòng) hoặc Id dòng đã có; bỏ trống = server cấp.</param>
public sealed record ScriptTreeLine(
    Guid? Id,
    ScriptLineKind Kind,
    string? SpeakerLabel,
    string? Text,
    IReadOnlyList<ScriptLineRefInput>? Refs = null);

/// <param name="CastRevisions">null = giữ nguyên phiên bản Bible của panel; [] = gỡ hết.</param>
public sealed record ScriptTreePanel(
    Guid? Id,
    int Number,
    string? Direction,
    IReadOnlyList<ScriptTreeCast>? CastRevisions = null,
    IReadOnlyList<ScriptTreeLine>? Lines = null);

public sealed record ScriptTreePage(Guid? Id, int Number, IReadOnlyList<ScriptTreePanel>? Panels = null);

/// <param name="ExpectedRevisionId">Id phiên bản kịch bản mới nhất client đang sửa; null khi chương chưa có phiên bản. Lệch thì 409.</param>
public sealed record ChapterScriptBody(Guid? ExpectedRevisionId, IReadOnlyList<ScriptTreePage>? Pages);

/// <param name="RevisionId">Phiên bản kịch bản sau lần lưu (bản mới, hoặc bản gần nhất nếu nội dung không đổi).</param>
/// <param name="Created">true = lần lưu này tạo phiên bản mới.</param>
public sealed record SaveChapterScriptResult(Guid RevisionId, int VersionNumber, bool Created, IReadOnlyList<string> Warnings);
