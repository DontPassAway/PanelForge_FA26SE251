using Microsoft.EntityFrameworkCore;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.ScriptLines.Common;

/// <summary>Luật dùng chung của viết script thủ công (UC-06): quyền, khoá chương, xung đột phiên bản, speaker, lịch sử revision.</summary>
internal static class ScriptLineRules
{
    public const int MaxTextLength = 4000;

    /// <summary>Nội dung hợp lệ: có thoại hoặc chỉ dẫn, không quá dài. Trả lỗi hoặc null.</summary>
    public static string? ValidateContent(string? dialogue, string? stageDirection)
    {
        if (string.IsNullOrWhiteSpace(dialogue) && string.IsNullOrWhiteSpace(stageDirection))
            return "Dòng script phải có thoại hoặc chỉ dẫn cảnh.";
        if ((dialogue?.Length ?? 0) > MaxTextLength || (stageDirection?.Length ?? 0) > MaxTextLength)
            return $"Thoại và chỉ dẫn tối đa {MaxTextLength} ký tự.";
        return null;
    }

    /// <summary>
    /// Chapter phải còn InProduction (BR-12), version khớp nếu client gửi (BR-11), và nếu Chapter đã giao việc Writer thì người sửa
    /// phải là người được giao ở trạng thái làm việc (cùng bộ luật với AI workflow; Producer luôn được).
    /// </summary>
    public static async Task<Result<(Chapter Chapter, Guid WorkspaceId)>> AuthorizeAsync(
        IPanelForgeDbContext db, IWorkspaceAuthorizationService auth, Guid userId, Guid chapterId, long? expectedVersion, CancellationToken ct)
    {
        var chapter = await db.Chapters.FirstOrDefaultAsync(c => c.Id == chapterId, ct);
        if (chapter is null)
            return Result<(Chapter, Guid)>.Failure($"Chapter {chapterId} không tồn tại.", ResultErrorCodes.NotFound);

        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked)
            return Result<(Chapter, Guid)>.Failure(locked, ResultErrorCodes.Conflict);

        if (expectedVersion is { } v && v != chapter.VersionVector)
            return Result<(Chapter, Guid)>.Failure(
                $"Chapter đã đổi (version hiện tại {chapter.VersionVector}, bạn đang sửa bản {v}). Tải lại rồi thử lại.", ResultErrorCodes.Conflict);

        var workspaceId = await db.Series.AsNoTracking().Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstAsync(ct);
        var role = await auth.GetMemberRoleAsync(userId, workspaceId, ct);
        if (role is not null and not WorkspaceRole.Producer)
        {
            var gate = await AiWorkflowFacts.EvaluateAssignmentAsync(db, userId, chapter, role.Value, ct);
            if (gate.BlockedReason is not null)
                return Result<(Chapter, Guid)>.Failure(gate.BlockedReason, ResultErrorCodes.Forbidden);
        }

        return Result<(Chapter, Guid)>.Success((chapter, workspaceId));
    }

    /// <summary>Speaker phải là mục Nhân vật trong Bible của đúng Series. Trả lỗi hoặc null.</summary>
    public static async Task<string?> ValidateSpeakerAsync(IPanelForgeDbContext db, Guid seriesId, Guid? speakerId, CancellationToken ct)
    {
        if (speakerId is null) return null;
        var ok = await db.BibleEntries.AsNoTracking().AnyAsync(e =>
            e.Id == speakerId && e.Category == BibleEntryCategory.Character &&
            db.SeriesBibles.Any(b => b.Id == e.SeriesBibleId && b.SeriesId == seriesId), ct);
        return ok ? null : "Speaker phải là một mục Nhân vật trong Series Bible của Series này.";
    }

    /// <summary>UC-06: mục Bible liên kết phải thuộc Bible của đúng Series, tối đa <see cref="ScriptLineBibleLink.MaxPerLine"/>. Trả lỗi hoặc null.</summary>
    public static async Task<string?> ValidateBibleEntriesAsync(
        IPanelForgeDbContext db, Guid seriesId, IReadOnlyList<Guid>? ids, CancellationToken ct)
    {
        if (ids is null || ids.Count == 0) return null;
        var distinct = ids.Distinct().ToList();
        if (distinct.Count > ScriptLineBibleLink.MaxPerLine)
            return $"Mỗi dòng script liên kết tối đa {ScriptLineBibleLink.MaxPerLine} mục Bible.";
        var found = await db.BibleEntries.AsNoTracking().CountAsync(e => distinct.Contains(e.Id) &&
            db.SeriesBibles.Any(b => b.Id == e.SeriesBibleId && b.SeriesId == seriesId), ct);
        return found == distinct.Count ? null : "Mục Bible liên kết phải thuộc Series Bible của Series này.";
    }

    /// <summary>
    /// Đặt tập liên kết Bible của dòng = <paramref name="ids"/> (null = giữ nguyên, [] = gỡ hết).
    /// Gỡ = xoá mềm (giữ lịch sử), thêm = tạo mới. Trả danh sách đang hoạt động sau thay đổi.
    /// </summary>
    public static async Task<IReadOnlyList<Guid>> SetBibleLinksAsync(
        IPanelForgeDbContext db, Guid lineId, IReadOnlyList<Guid>? ids, CancellationToken ct)
    {
        var current = await db.ScriptLineBibleLinks.Where(k => k.ScriptLineId == lineId).ToListAsync(ct);
        if (ids is null) return current.Select(k => k.BibleEntryId).ToList();

        var wanted = ids.Distinct().ToList();
        foreach (var k in current.Where(k => !wanted.Contains(k.BibleEntryId)))
            db.ScriptLineBibleLinks.Remove(k);
        foreach (var id in wanted.Except(current.Select(k => k.BibleEntryId)))
            db.ScriptLineBibleLinks.Add(ScriptLineBibleLink.Create(lineId, id));
        return wanted;
    }

    public static List<BaselineScriptLine> Snapshot(IEnumerable<ScriptLine> lines) =>
        lines.OrderBy(l => l.LineOrder)
            .Select(l => new BaselineScriptLine(l.Id, l.LineOrder, l.SpeakerCharacterId, l.DialogueText, l.StageDirection)).ToList();

    /// <summary>
    /// Ghi lịch sử append-only (BR-01) cùng lần Save với thay đổi: nếu Scene chưa có revision mà đã có dòng thì chụp baseline trước;
    /// rồi thêm revision Script mới = toàn bộ dòng sau thay đổi. Tăng VersionVector để đề xuất AI cũ thành stale (409).
    /// </summary>
    public static async Task<ContentRevision> RecordRevisionAsync(
        IPanelForgeDbContext db, Chapter chapter, Guid sceneId, string summary,
        IReadOnlyList<BaselineScriptLine> before, IReadOnlyList<BaselineScriptLine> after, CancellationToken ct)
    {
        var latest = await db.ContentRevisions.Where(r => r.SceneId == sceneId && r.Kind == ContentRevisionKind.Script)
            .OrderByDescending(r => r.VersionNumber).FirstOrDefaultAsync(ct);

        var previousId = latest?.Id;
        var next = (latest?.VersionNumber ?? 0) + 1;
        if (latest is null && before.Count > 0)
        {
            var baselinePayload = AiJson.Serialize(before);
            var baseline = ContentRevision.Create(chapter.Id, sceneId, ContentRevisionKind.Script, 1,
                "Baseline: script có sẵn trước khi sửa tay", baselinePayload, AiContentValidator.Sha256(baselinePayload), null, null);
            db.ContentRevisions.Add(baseline);
            previousId = baseline.Id;
            next = 2;
        }

        var payload = AiJson.Serialize(after);
        var revision = ContentRevision.Create(chapter.Id, sceneId, ContentRevisionKind.Script, next, summary,
            payload, AiContentValidator.Sha256(payload), previousId, null);
        db.ContentRevisions.Add(revision);
        chapter.IncrementVersion();
        return revision;
    }
}
