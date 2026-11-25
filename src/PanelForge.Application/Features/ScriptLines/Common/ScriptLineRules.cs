using System.Text.Json;
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

    /// <summary>Chữ của dòng như Lettering so khớp (BR-05): thoại, không có thì chỉ dẫn.</summary>
    public static string? LetteringText(ScriptLine line) => line.DialogueText ?? line.StageDirection;

    /// <summary>
    /// CF4 (BR-05, BR-06): Writer sửa/xoá dòng đã đặt bóng → việc Lettering đã duyệt của đúng các Panel có bóng gắn dòng đó phải
    /// làm lại. Panel của bóng = PanelId của element, không có thì Panel neo dòng, không có nữa thì các Panel cùng trang với bóng.
    /// Gọi trước SaveChanges của thao tác sửa (BR-18). Trả lỗi hoặc null.
    /// </summary>
    public static async Task<string?> ReworkApprovedLetteringAsync(
        IPanelForgeDbContext db, IPageRepository pages, Guid chapterId, ScriptLine line, string reason, CancellationToken ct)
    {
        // Lưới V3: ô của công đoạn Letterer đã duyệt (sẽ mở lại ô nằm trên trang có bóng gắn dòng này)
        var gridLettering = await (from t in db.StageTasks
                                   join s in db.PipelineStages.AsNoTracking() on t.PipelineStageId equals s.Id
                                   where t.ChapterId == chapterId && t.Status == StageTaskStatus.Approved && !t.IsInputOutdated
                                         && s.AllowedRole == WorkspaceRole.Letterer
                                   select t).ToListAsync(ct);
        var v2Approved = await db.Assignments.AnyAsync(a => a.ChapterId == chapterId && a.Status == AssignmentStatus.Approved
                                                            && a.AssignedRole == WorkspaceRole.Letterer, ct);
        // Không có việc Lettering nào đã duyệt thì khỏi dựng lại canvas
        if (!v2Approved && gridLettering.Count == 0)
            return null;

        var pageIds = await db.Pages.AsNoTracking().Where(p => p.ChapterId == chapterId).Select(p => p.Id).ToListAsync(ct);
        var panelIds = new HashSet<Guid>();
        foreach (var pageId in pageIds)
        {
            var page = await pages.GetAsync(pageId, ct);
            var bound = page?.Elements.Values.Where(e => !e.IsRemoved && e.ScriptLineId == line.Id).ToList() ?? [];
            if (bound.Count == 0) continue;

            foreach (var cell in gridLettering.Where(t => t.PageId == pageId)) cell.Reopen("Thoại trên trang đã thay đổi, cần dàn chữ lại.");

            var pagePanels = await db.Panels.AsNoTracking().Where(p => p.PageId == pageId).Select(p => p.Id).ToListAsync(ct);
            foreach (var element in bound)
            {
                if (element.PanelId is { } own) panelIds.Add(own);
                else if (line.PanelId is { } anchor && pagePanels.Contains(anchor)) panelIds.Add(anchor);
                else panelIds.UnionWith(pagePanels);
            }
        }

        if (!v2Approved) return null;
        var (_, error) = await StageProgressRules.ReworkForSourceChangeAsync(
            db, chapterId, panelIds, [WorkspaceRole.Letterer], reason, ct);
        return error;
    }

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
    /// Kiểm tra quyền sửa kịch bản (BR-07, Q24, §5.1):
    /// - Producer chỉ đọc kịch bản (403 Forbidden); không có quyền chỉnh sửa, lưu, xóa dòng, Page, Panel.
    /// - Artist / Letterer / Editor luôn 403 Forbidden kể cả khi có assignment.
    /// - Chỉ Writer mới có quyền chỉnh sửa / lưu kịch bản.
    /// - Nếu Chapter đã giao kịch bản cho Writer cụ thể thì chỉ Writer được giao mới được sửa.
    /// - Khi kịch bản đã đóng băng (Frozen) hoặc Chapter không còn chỉnh sửa được -> 409 Conflict.
    /// - Phiên bản không khớp -> 409 Conflict.
    /// </summary>
    public static async Task<Result<(Chapter Chapter, Guid WorkspaceId)>> AuthorizeAsync(
        IPanelForgeDbContext db, IWorkspaceAuthorizationService auth, Guid userId, Guid chapterId, long? expectedVersion, CancellationToken ct)
    {
        var chapter = await db.Chapters.FirstOrDefaultAsync(c => c.Id == chapterId, ct);
        if (chapter is null)
            return Result<(Chapter, Guid)>.Failure($"Chapter {chapterId} không tồn tại.", ResultErrorCodes.NotFound);

        var workspaceId = await db.Series.AsNoTracking().Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstAsync(ct);
        var role = await auth.GetMemberRoleAsync(userId, workspaceId, ct);
        if (role is null)
            return Result<(Chapter, Guid)>.Failure("Bạn không phải thành viên của Workspace này.", ResultErrorCodes.Forbidden);

        // Q24 & BR-07: Producer chỉ đọc kịch bản (403). Sửa/xoá/sắp xếp dòng, Page, Panel của kịch bản: chỉ Writer.
        if (role == WorkspaceRole.Producer)
            return Result<(Chapter, Guid)>.Failure(
                "Producer chỉ có quyền xem và nhận xét kịch bản, không có quyền chỉnh sửa (BR-07, Q24).",
                ResultErrorCodes.Forbidden);

        // Spec §5.1: Artist/Letterer/Editor luôn 403 đối với thao tác sửa kịch bản ngay cả khi có assignment.
        if (role != WorkspaceRole.Writer)
            return Result<(Chapter, Guid)>.Failure(
                $"Vai trò {role} không có quyền chỉnh sửa kịch bản. Chỉ Writer mới có quyền chỉnh sửa (BR-07, §5.1).",
                ResultErrorCodes.Forbidden);

        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked)
            return Result<(Chapter, Guid)>.Failure(locked, ResultErrorCodes.Conflict);

        // D12: chỉ Frozen khoá sửa; InReview Writer vẫn sửa (Editor đọc bản đã nộp)
        if (chapter.ScriptLockReason is { } scriptLocked)
            return Result<(Chapter, Guid)>.Failure(scriptLocked, ResultErrorCodes.Conflict);

        if (expectedVersion is { } v && v != chapter.VersionVector)
            return Result<(Chapter, Guid)>.Failure(
                $"Chapter đã đổi (version hiện tại {chapter.VersionVector}, bạn đang sửa bản {v}). Tải lại rồi thử lại.", ResultErrorCodes.Conflict);

        // Khi chương đã giao kịch bản cho Writer cụ thể thì chỉ (các) Writer đó được sửa; chưa giao ai thì vẫn tự do.
        var writerIds = await db.Assignments.AsNoTracking()
            .Where(a => a.ChapterId == chapter.Id && a.AssignedRole == WorkspaceRole.Writer && a.CancelReason == null)
            .Select(a => a.AssigneeUserId).ToListAsync(ct);
        // Lưới V3: lô Writer còn hiệu lực của chương cũng là "đã giao kịch bản"
        writerIds.AddRange(await (from t in db.StageTasks.AsNoTracking()
                                  join l in db.StageTaskLots.AsNoTracking() on t.LotId equals (Guid?)l.Id
                                  where t.ChapterId == chapter.Id && l.AssignedRole == WorkspaceRole.Writer
                                        && t.Status != StageTaskStatus.Cancelled
                                  select l.AssigneeUserId).Distinct().ToListAsync(ct));
        if (writerIds.Count > 0 && !writerIds.Contains(userId))
            return Result<(Chapter, Guid)>.Failure(
                "Kịch bản chương này đã được giao cho Writer khác. Chỉ Writer được giao mới có quyền chỉnh sửa (BR-07).",
                ResultErrorCodes.Forbidden);

        return Result<(Chapter, Guid)>.Success((chapter, workspaceId));
    }

    /// <summary>
    /// Writer tạo Page/Panel khi lưu kịch bản (khung xương Trang → Panel để neo dòng thoại) theo đúng luật sửa ScriptLine:
    /// chương còn sản xuất, kịch bản chưa đóng băng, và nếu chương đã giao Writer thì chỉ người được giao.
    /// Vai trò khác giữ luật cũ của endpoint (Producer / Artist + WorkScopeGuard). Trả (lý do, mã lỗi) hoặc null.
    /// </summary>
    public static async Task<(string Error, string? Code)?> WriterStructureBlockedReasonAsync(
        IPanelForgeDbContext db, IWorkspaceAuthorizationService auth, Guid userId, Guid chapterId, CancellationToken ct)
    {
        var seriesId = await db.Chapters.AsNoTracking().Where(c => c.Id == chapterId).Select(c => (Guid?)c.SeriesId).FirstOrDefaultAsync(ct);
        if (seriesId is null) return null; // handler tự báo không tồn tại
        var workspaceId = await db.Series.AsNoTracking().Where(s => s.Id == seriesId).Select(s => s.WorkspaceId).FirstAsync(ct);
        if (await auth.GetMemberRoleAsync(userId, workspaceId, ct) != WorkspaceRole.Writer) return null;

        var authorized = await AuthorizeAsync(db, auth, userId, chapterId, expectedVersion: null, ct);
        return authorized.IsSuccess ? null : (authorized.ErrorMessage!, authorized.ErrorCode);
    }

    public static List<BaselineScriptLine> Snapshot(IEnumerable<ScriptLine> lines) =>
        lines.OrderBy(l => l.LineOrder)
            .Select(l => new BaselineScriptLine(l.Id, l.LineOrder, null, l.DialogueText, l.StageDirection)).ToList();

    /// <summary>
    /// Ghi lịch sử append-only (BR-01) cùng lần Save với thay đổi: nếu Scene chưa có revision mà đã có dòng thì chụp baseline trước;
    /// rồi thêm revision Script mới = toàn bộ dòng sau thay đổi. Đề xuất AI cũ thành stale (409) nhờ revision mới này.
    /// </summary>
    public static async Task<ContentRevision> RecordRevisionAsync(
        IPanelForgeDbContext db, Chapter chapter, Guid sceneId, string summary,
        IReadOnlyList<BaselineScriptLine> before, IReadOnlyList<BaselineScriptLine> after, CancellationToken ct,
        string? mappingJson = null)
    {
        var latest = await db.ContentRevisions.Where(r => r.SceneId == sceneId && r.Kind == ContentRevisionKind.Script)
            .OrderByDescending(r => r.VersionNumber).FirstOrDefaultAsync(ct);

        var payload = AiJson.Serialize(after);
        var hash = AiContentValidator.Sha256(payload);
        // Lưu lại mà nội dung dòng không đổi (vd. chỉ đổi Bible link / chế độ lời dẫn, hoặc blur không sửa gì):
        // không sinh version trùng, không tăng VersionVector. Revision mới nhất vẫn là bản đúng.
        if (latest is not null && latest.ContentHash == hash) return latest;

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
            if (baseline.ContentHash == hash) return baseline;
        }

        var revision = ContentRevision.Create(chapter.Id, sceneId, ContentRevisionKind.Script, next, summary,
            payload, hash, previousId, null, mappingJson);
        db.ContentRevisions.Add(revision);
        // Quy ước version: lưu tay trong phiên không tăng VersionVector, xung đột với AI được phát hiện qua revision Script.
        // VersionVector chỉ tăng khi Accept đề xuất AI hoặc khi kịch bản bị trả lại để viết lại.
        return revision;
    }

    /// <summary>
    /// Đọc các dòng của một revision Script. Payload có hai dạng: mảng dòng (sửa tay, baseline) hoặc object có <c>lines</c>
    /// (đề xuất AI). Trả null khi payload không đọc được.
    /// </summary>
    public static IReadOnlyList<BaselineScriptLine>? ParseRevisionLines(string payloadJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("lines", out var lines)) root = lines;
            if (root.ValueKind != JsonValueKind.Array) return null;
            return root.Deserialize<List<BaselineScriptLine>>(AiJson.Options)?.OrderBy(l => l.Order).ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>SCR-05: Lấy Scene mặc định của Chapter, tạo nếu chưa có (D2: Scene ẩn với người dùng)</summary>
    public static async Task<Scene> EnsureDefaultSceneAsync(IPanelForgeDbContext db, Guid chapterId, CancellationToken ct = default)
    {
        var scene = await db.Scenes.FirstOrDefaultAsync(s => s.ChapterId == chapterId, ct);
        if (scene is null)
        {
            scene = Scene.CreateDefault(chapterId);
            db.Scenes.Add(scene);
            await db.SaveChangesAsync(ct);
        }
        return scene;
    }
}
