using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Chapters.Services;
using PanelForge.Application.Features.Pages.Commands.SyncScriptStructure;
using PanelForge.Application.Features.ScriptLines.Common;
using PanelForge.Application.Features.ScriptLines.Models;
using PanelForge.Application.Features.ScriptVersions;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.ScriptLines.Commands.SaveChapterScript;

/// <summary>Lỗi nghiệp vụ giữa chừng lưu cả chương: ném ra để transaction rollback toàn bộ (Marten + EF), handler đổi thành Result.Failure.</summary>
internal sealed class ScriptSaveAbortException : Exception
{
    public string Code { get; }

    public ScriptSaveAbortException(string message, string? code) : base(message)
    {
        Code = code ?? ResultErrorCodes.Validation;
    }
}

internal sealed record ChapterScriptSaveOutcome(
    Chapter Chapter, Guid WorkspaceId, ScriptVersion Version, bool Created, IReadOnlyList<string> Warnings);

/// <summary>
/// SCR-14: lưu cả cây Trang → Panel → dòng của chương trong một transaction. Thứ tự: quyền Writer → expectedRevisionId →
/// kiểm payload → upsert Page/Panel → dòng (thêm/sửa/xoá mềm) → tham chiếu Bible → hướng panel → cast → chụp phiên bản.
/// Lỗi bất kỳ ném <see cref="ScriptSaveAbortException"/> để rollback.
/// </summary>
internal static class ChapterScriptSaver
{
    public static async Task<ChapterScriptSaveOutcome> SaveAsync(
        IPanelForgeDbContext db,
        IWorkspaceAuthorizationService auth,
        IPageRepository pageRepository,
        IChapterCastService castService,
        Guid chapterId,
        Guid userId,
        ChapterScriptBody body,
        string? message,
        CancellationToken ct)
    {
        var authorized = await ScriptLineRules.AuthorizeAsync(db, auth, userId, chapterId, null, ct);
        if (!authorized.IsSuccess)
            throw new ScriptSaveAbortException(authorized.ErrorMessage!, authorized.ErrorCode);
        var (chapter, workspaceId) = authorized.Value;

        var latest = await db.ScriptVersions.AsNoTracking()
            .Where(v => v.ChapterId == chapterId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);
        if (body.ExpectedRevisionId != latest?.Id)
            throw new ScriptSaveAbortException(
                "Kịch bản đã được lưu ở phiên bản khác kể từ lần bạn tải. Tải lại rồi thử lại.", ResultErrorCodes.Conflict);

        var payloadPages = (body.Pages ?? []).OrderBy(p => p.Number).ToList();
        await ValidatePayloadAsync(db, chapterId, payloadPages, ct);

        var pages = await db.Pages.Where(p => p.ChapterId == chapterId).ToListAsync(ct);
        var pageIds = pages.Select(p => p.Id).ToList();
        var panels = await db.Panels.Where(p => pageIds.Contains(p.PageId)).ToListAsync(ct);
        var existingLines = await db.ScriptLines.Where(l => l.Scene.ChapterId == chapterId).ToListAsync(ct);
        var existingMap = existingLines.ToDictionary(l => l.Id);

        var unknownIds = payloadPages.SelectMany(p => p.Panels ?? []).SelectMany(p => p.Lines ?? [])
            .Where(l => l.Id.HasValue && !existingMap.ContainsKey(l.Id.Value)).Select(l => l.Id!.Value).ToList();
        if (unknownIds.Count > 0 && await db.ScriptLines.IgnoreQueryFilters().AnyAsync(l => unknownIds.Contains(l.Id), ct))
            throw new ScriptSaveAbortException("Id dòng đã được dùng ở nơi khác.", ResultErrorCodes.Conflict);

        var scene = await ScriptLineRules.EnsureDefaultSceneAsync(db, chapterId, ct);
        var bible = await ScriptBibleRefs.GetOrAddBibleAsync(db, chapter.SeriesId, ct);

        var warnings = new List<string>();
        var linksByLine = new Dictionary<Guid, IReadOnlyList<Guid>>();
        var liveLineIds = new HashSet<Guid>();
        var touchedPanelIds = new HashSet<Guid>();
        var modifiedLines = new List<ScriptLine>();
        var placedPanels = new List<(Panel Panel, ScriptTreePanel Wanted)>();
        var order = 0;

        foreach (var wantedPage in payloadPages)
        {
            var (page, _) = await ScriptStructureRules.EnsurePageAsync(db, pageRepository, pages, chapterId, wantedPage.Number, ct);

            foreach (var wantedPanel in (wantedPage.Panels ?? []).OrderBy(p => p.Number))
            {
                var (panel, _) = ScriptStructureRules.EnsurePanel(db, panels, page.Id, wantedPanel.Number, userId);
                touchedPanelIds.Add(panel.Id);
                placedPanels.Add((panel, wantedPanel));

                var lineIndex = 0;
                foreach (var wantedLine in wantedPanel.Lines ?? [])
                {
                    order++;
                    lineIndex++;
                    var label = $"Trang {wantedPage.Number}, panel {wantedPanel.Number}, dòng {lineIndex}";
                    var line = UpsertLine(db, existingMap, scene, panel.Id, order, wantedLine, label, modifiedLines);
                    liveLineIds.Add(line.Id);

                    if (await ScriptBibleRefs.ApplyAsync(db, bible, line, label, wantedLine.Refs, null, warnings, linksByLine, ct) is { } refError)
                        throw new ScriptSaveAbortException(refError, ResultErrorCodes.Validation);
                }
            }
        }

        foreach (var toDelete in existingLines.Where(l => !liveLineIds.Contains(l.Id)))
        {
            toDelete.DeletedBy = userId.ToString();
            db.ScriptLines.Remove(toDelete);
            if (await ScriptLineRules.ReworkApprovedLetteringAsync(db, pageRepository, chapterId, toDelete,
                    "Writer đã xoá dòng thoại trong lần lưu kịch bản; cần cập nhật bóng thoại.", ct) is { } reworkError)
                throw new ScriptSaveAbortException(reworkError, ResultErrorCodes.Conflict);
        }
        foreach (var modified in modifiedLines)
        {
            if (await ScriptLineRules.ReworkApprovedLetteringAsync(db, pageRepository, chapterId, modified,
                    "Writer đã sửa thoại trong lần lưu kịch bản; cần cập nhật bóng thoại theo script mới.", ct) is { } reworkError)
                throw new ScriptSaveAbortException(reworkError, ResultErrorCodes.Conflict);
        }

        await RemoveDroppedStructureAsync(db, pageRepository, pages, panels, payloadPages, touchedPanelIds, userId, warnings, ct);

        var variants = await db.PanelBibleVariants.Where(v => touchedPanelIds.Contains(v.PanelId)).ToListAsync(ct);
        foreach (var (panel, wanted) in placedPanels)
        {
            if (ScriptStructureRules.ApplyDirection(panel, wanted.Direction, userId) is { } directionError)
                throw new ScriptSaveAbortException($"Panel {wanted.Number}: {directionError}", ResultErrorCodes.Validation);

            if (wanted.CastRevisions is not null)
            {
                ScriptStructureRules.ApplyCastRevisions(
                    db, panel.Id,
                    wanted.CastRevisions.Select(c => new PanelCastRevisionItem(c.EntryId, c.RevisionId)).ToList(),
                    variants.Where(v => v.PanelId == panel.Id).ToList(), userId);
            }
        }

        ScriptVersion version;
        try
        {
            // Snapshot đọc từ DB nên phải ghi khung và dòng trước; tất cả vẫn nằm trong transaction của handler.
            await db.SaveChangesAsync(ct);
            version = await ScriptSnapshotBuilder.CommitAsync(db, chapterId, userId, message, ct, castService);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ScriptSaveAbortException("Kịch bản vừa được người khác sửa. Tải lại rồi thử lại.", ResultErrorCodes.Conflict);
        }

        return new ChapterScriptSaveOutcome(chapter, workspaceId, version, version.Id != latest?.Id, warnings.Distinct().ToList());
    }

    private static ScriptLine UpsertLine(
        IPanelForgeDbContext db, Dictionary<Guid, ScriptLine> existingMap, Scene scene, Guid panelId, int order,
        ScriptTreeLine wanted, string label, List<ScriptLine> modifiedLines)
    {
        var text = wanted.Text!.Trim();
        var speakerLabel = string.IsNullOrWhiteSpace(wanted.SpeakerLabel) ? null : wanted.SpeakerLabel.Trim();
        try
        {
            if (wanted.Id is { } id && existingMap.TryGetValue(id, out var line))
            {
                var textBefore = ScriptLineRules.LetteringText(line);
                if (line.Text != text || line.Kind != wanted.Kind || line.SpeakerLabel != speakerLabel)
                {
                    var keepSpeaker = line.Kind == wanted.Kind && line.SpeakerLabel == speakerLabel ? line.SpeakerCharacterId : null;
                    line.UpdateContent(null, null, keepSpeaker, line.NarrationMode, wanted.Kind, speakerLabel, text);
                }
                line.Reorder(order);
                if (line.SceneId != scene.Id) line.MoveToScene(scene.Id);
                line.AssignToPanel(panelId);
                if (ScriptLineRules.LetteringText(line) != textBefore) modifiedLines.Add(line);
                return line;
            }

            var created = ScriptLine.Create(
                scene.Id, order, null, null, null, wanted.Id, scene.DefaultNarrationMode, wanted.Kind, speakerLabel, text);
            created.AssignToPanel(panelId);
            db.ScriptLines.Add(created);
            return created;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            throw new ScriptSaveAbortException($"{label}: {ex.Message}", ResultErrorCodes.Validation);
        }
    }

    /// <summary>
    /// Page/Panel không còn trong payload: có artwork thì giữ lại kèm cảnh báo, còn lại xoá mềm như SyncScriptStructure
    /// (việc đang giao thì từ chối 409 thay vì xoá).
    /// </summary>
    private static async Task RemoveDroppedStructureAsync(
        IPanelForgeDbContext db, IPageRepository pageRepository, List<Page> pages, List<Panel> panels,
        IReadOnlyList<ScriptTreePage> payloadPages, HashSet<Guid> touchedPanelIds, Guid userId, List<string> warnings, CancellationToken ct)
    {
        var listedPageNumbers = payloadPages.Select(p => p.Number).ToHashSet();
        var droppedPages = pages.Where(p => !listedPageNumbers.Contains(p.PageNumber)).ToList();
        var droppedPanels = panels.Where(p => !touchedPanelIds.Contains(p.Id)).ToList();
        if (droppedPages.Count == 0 && droppedPanels.Count == 0) return;

        var droppedPageIds = droppedPages.Select(p => p.Id).ToList();
        var droppedPanelIds = droppedPanels.Select(p => p.Id).ToList();
        var artworkRefs = await db.AssetReferences.AsNoTracking()
            .Where(r => droppedPageIds.Contains(r.PageId) || (r.PanelId != null && droppedPanelIds.Contains(r.PanelId.Value)))
            .Select(r => new { r.PageId, r.PanelId })
            .ToListAsync(ct);
        var pagesWithArtwork = artworkRefs.Select(r => r.PageId).ToHashSet();
        var panelsWithArtwork = artworkRefs.Where(r => r.PanelId != null).Select(r => r.PanelId!.Value).ToHashSet();

        var keptPages = droppedPages.Where(p => pagesWithArtwork.Contains(p.Id)).ToList();
        var keptPageIds = keptPages.Select(p => p.Id).ToHashSet();
        var removedPages = droppedPages.Except(keptPages).ToList();
        var pageNumberById = pages.ToDictionary(p => p.Id, p => p.PageNumber);
        var keptPanels = droppedPanels
            .Where(p => panelsWithArtwork.Contains(p.Id) || keptPageIds.Contains(p.PageId)).ToList();
        var removedPanels = droppedPanels.Except(keptPanels).ToList();

        foreach (var page in keptPages)
            warnings.Add($"Trang {page.PageNumber} đã có artwork nên được giữ lại dù không còn trong kịch bản.");
        foreach (var panel in keptPanels.Where(p => !keptPageIds.Contains(p.PageId)))
            warnings.Add($"Panel {panel.PanelNumber} của trang {pageNumberById[panel.PageId]} đã có artwork nên được giữ lại dù không còn trong kịch bản.");

        var removedTargets = removedPages.Select(p => p.Id).Concat(removedPanels.Select(p => p.Id)).ToList();
        if (removedTargets.Count > 0 &&
            await AssignmentRules.DeleteBlockedReasonAsync(db, "trang", removedTargets, ct) is { } blocked)
            throw new ScriptSaveAbortException(blocked, ResultErrorCodes.Conflict);

        await ScriptStructureRules.RemoveAsync(db, pageRepository, removedPages, removedPanels, userId, ct);
    }

    private static async Task ValidatePayloadAsync(
        IPanelForgeDbContext db, Guid chapterId, IReadOnlyList<ScriptTreePage> pages, CancellationToken ct)
    {
        static ScriptSaveAbortException Invalid(string message) => new(message, ResultErrorCodes.Validation);

        if (pages.Any(p => p.Number <= 0) || pages.Select(p => p.Number).Distinct().Count() != pages.Count)
            throw Invalid("Số trang phải dương và không trùng nhau.");

        var lineIds = new HashSet<Guid>();
        var casts = new List<PanelCastRevisionItem>();
        foreach (var page in pages)
        {
            var panels = page.Panels ?? [];
            if (panels.Any(p => p.Number <= 0) || panels.Select(p => p.Number).Distinct().Count() != panels.Count)
                throw Invalid($"Trang {page.Number}: số panel phải dương và không trùng nhau.");

            foreach (var panel in panels)
            {
                if (panel.CastRevisions is { } cast)
                {
                    if (cast.Select(c => c.EntryId).Distinct().Count() != cast.Count)
                        throw Invalid($"Trang {page.Number}, panel {panel.Number}: mỗi mục Bible chỉ có một phiên bản.");
                    casts.AddRange(cast.Select(c => new PanelCastRevisionItem(c.EntryId, c.RevisionId)));
                }

                var index = 0;
                foreach (var line in panel.Lines ?? [])
                {
                    index++;
                    var label = $"Trang {page.Number}, panel {panel.Number}, dòng {index}";
                    if (line.Id is { } id && !lineIds.Add(id))
                        throw Invalid($"{label}: Id dòng bị trùng trong payload.");
                    if (!Enum.IsDefined(line.Kind))
                        throw Invalid($"{label}: loại dòng không hợp lệ.");
                    if (ScriptLineRules.ValidateContent(line.Text, null) is { } invalid)
                        throw Invalid($"{label}: {invalid}");
                    if (line.Kind == ScriptLineKind.Action && !string.IsNullOrWhiteSpace(line.SpeakerLabel))
                        throw Invalid($"{label}: dòng Action không có người nói.");
                    if (ScriptBibleRefs.ValidateKind(line.Refs, line.Kind, label) is { } kindError)
                        throw Invalid(kindError);
                }
            }
        }

        if (await ScriptStructureRules.ValidateCastRevisionsAsync(db, chapterId, casts, ct) is { } castError)
            throw Invalid(castError);
    }
}
