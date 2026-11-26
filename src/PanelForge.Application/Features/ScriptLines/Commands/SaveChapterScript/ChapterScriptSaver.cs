using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Chapters.Services;
using PanelForge.Application.Features.ScriptLines.Common;
using PanelForge.Application.Features.ScriptLines.Models;
using PanelForge.Application.Features.ScriptVersions;
using PanelForge.Application.Features.TaskGrid.Commands.GenerateStageTasks;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.ScriptLines.Commands.SaveChapterScript;

/// <summary>Lỗi nghiệp vụ giữa chừng lưu cả chương: ném ra để transaction rollback toàn bộ (Marten + EF), handler đổi thành Result.Failure.</summary>
internal sealed class ScriptSaveAbortException : Exception
{
    public string Code { get; }
    public object? Details { get; }

    public ScriptSaveAbortException(string message, string? code, object? details = null) : base(message)
    {
        Code = code ?? ResultErrorCodes.Validation;
        Details = details;
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
        var claimedPanelIds = payloadPages
            .SelectMany(p => p.Panels ?? [])
            .Where(p => p.Id.HasValue)
            .Select(p => p.Id!.Value)
            .ToHashSet();
        var nextTemporaryPanelNumber = panels.Count == 0
            ? 1
            : panels.Max(p => p.PanelNumber) + payloadPages.Sum(p => p.Panels?.Count ?? 0) + 1;
        var order = 0;

        foreach (var wantedPage in payloadPages)
        {
            var (page, _) = await ScriptStructureRules.EnsurePageAsync(db, pageRepository, pages, chapterId, wantedPage.Number, ct);

            foreach (var wantedPanel in (wantedPage.Panels ?? []).OrderBy(p => p.Number))
            {
                Panel panel;
                try
                {
                    (panel, _) = ScriptStructureRules.EnsurePanel(
                        db,
                        panels,
                        page.Id,
                        wantedPanel.Id,
                        wantedPanel.Number,
                        nextTemporaryPanelNumber++,
                        claimedPanelIds,
                        touchedPanelIds,
                        userId);
                }
                catch (InvalidOperationException ex)
                {
                    throw new ScriptSaveAbortException(
                        $"Trang {wantedPage.Number}, panel {wantedPanel.Number}: {ex.Message}",
                        ResultErrorCodes.Validation);
                }
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

                    if (await ScriptBibleRefs.ApplyAsync(db, bible, line, label, wantedLine.Refs, warnings, linksByLine, ct) is { } refError)
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

        await RemoveDroppedStructureAsync(
            db, pageRepository, chapter.Status, pages, panels, existingLines, payloadPages, touchedPanelIds, userId, warnings, ct);

        var variants = await db.PanelBibleVariants.Where(v => v.PanelId.HasValue && touchedPanelIds.Contains(v.PanelId.Value)).ToListAsync(ct);
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

        // SCR-42 (D9c): Manual Save / Submit đồng bộ & dọn working copy trong cùng transaction.
        var workingCopy = await db.ChapterScriptWorkingCopies.FirstOrDefaultAsync(w => w.ChapterId == chapterId, ct);
        if (workingCopy is not null)
            db.ChapterScriptWorkingCopies.Remove(workingCopy);

        ScriptVersion version;
        try
        {
            // Ghi panel mới bằng số tạm và hoàn tất xoá mềm trước, tránh đụng unique index khi chèn/xoá giữa trang.
            await db.SaveChangesAsync(ct);
            await NormalizePanelNumbersAsync(db, placedPanels, touchedPanelIds, ct);
            version = await ScriptSnapshotBuilder.CommitAsync(db, chapterId, userId, message, ct, castService);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ScriptSaveAbortException("Kịch bản vừa được người khác sửa. Tải lại rồi thử lại.", ResultErrorCodes.Conflict);
        }

        // Lưới V3: ở PreProduction, thêm/bớt trang thì đồng bộ ô ngay. Ở InProduction, RefreezeScript đảm nhận đồng bộ + giao lô.
        if (chapter.Status == ChapterStatus.PreProduction && await WorkScopeGuard.UsesGridAsync(db, chapterId, ct))
            await new GenerateStageTasksCommandHandler(db).Handle(new GenerateStageTasksCommand(chapterId, userId), ct);

        return new ChapterScriptSaveOutcome(chapter, workspaceId, version, version.Id != latest?.Id, warnings.Distinct().ToList());
    }

    private static async Task NormalizePanelNumbersAsync(
        IPanelForgeDbContext db,
        IReadOnlyList<(Panel Panel, ScriptTreePanel Wanted)> placedPanels,
        IReadOnlySet<Guid> touchedPanelIds,
        CancellationToken ct)
    {
        var retainedFinalNumbers = new List<(Panel Panel, int Number)>();
        foreach (var pageGroup in placedPanels.GroupBy(x => x.Panel.PageId))
        {
            var activePanels = await db.Panels
                .Where(p => p.PageId == pageGroup.Key)
                .ToListAsync(ct);
            var desiredNumbers = pageGroup.Select(x => x.Wanted.Number).ToHashSet();
            var retainedDroppedPanels = activePanels
                .Where(p => !touchedPanelIds.Contains(p.Id) && desiredNumbers.Contains(p.PanelNumber))
                .ToList();
            var needsReorder = pageGroup.Any(x => x.Panel.PanelNumber != x.Wanted.Number) ||
                               retainedDroppedPanels.Count > 0;
            if (!needsReorder) continue;

            var temporaryNumber = Math.Max(
                activePanels.Count == 0 ? 0 : activePanels.Max(p => p.PanelNumber),
                desiredNumbers.Count == 0 ? 0 : desiredNumbers.Max()) + activePanels.Count + 1;

            foreach (var (panel, _) in pageGroup)
                panel.Reorder(temporaryNumber++);
            var retainedFinalNumber = Math.Max(
                activePanels.Count == 0 ? 0 : activePanels.Max(p => p.PanelNumber),
                desiredNumbers.Count == 0 ? 0 : desiredNumbers.Max()) + 1;
            foreach (var retained in retainedDroppedPanels)
            {
                retained.Reorder(temporaryNumber++);
                retainedFinalNumbers.Add((retained, retainedFinalNumber++));
            }
        }

        // Pha 1 giải phóng toàn bộ số thứ tự cũ trong unique index.
        await db.SaveChangesAsync(ct);

        foreach (var (panel, wanted) in placedPanels)
            panel.Reorder(wanted.Number);
        foreach (var (panel, number) in retainedFinalNumbers)
            panel.Reorder(number);

        // Pha 2 gán lại số thứ tự cuối cùng đúng theo payload, nhưng giữ nguyên Panel ID và mọi artwork liên kết.
        await db.SaveChangesAsync(ct);
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
                    line.UpdateContent(null, null, line.NarrationMode, wanted.Kind, speakerLabel, text);
                }
                line.Reorder(order);
                if (line.SceneId != scene.Id) line.MoveToScene(scene.Id);
                line.AssignToPanel(panelId);
                if (ScriptLineRules.LetteringText(line) != textBefore) modifiedLines.Add(line);
                return line;
            }

            var created = ScriptLine.Create(
                scene.Id, order, null, null, wanted.Id, scene.DefaultNarrationMode, wanted.Kind, speakerLabel, text);
            created.AssignToPanel(panelId);
            db.ScriptLines.Add(created);
            return created;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            throw new ScriptSaveAbortException($"{label}: {ex.Message}", ResultErrorCodes.Validation);
        }
    }

    private static readonly StageTaskStatus[] NotStartedTaskStatuses =
        [StageTaskStatus.Cancelled, StageTaskStatus.Unassigned, StageTaskStatus.Assigned];

    /// <summary>
    /// Page/Panel không còn trong payload:
    /// - Khi InProduction: CHỈ cho phép xóa mềm nếu toàn bộ StageTasks đều NotStarted và chưa có artwork / bóng thoại;
    ///   ngược lại chặn 409 CANNOT_DELETE_ACTIVE_PRODUCTION_PAGE kèm blockedPageIds / blockedPanelIds.
    /// - Khi PreProduction: có artwork thì giữ lại kèm cảnh báo, còn lại xóa mềm.
    /// </summary>
    private static async Task RemoveDroppedStructureAsync(
        IPanelForgeDbContext db,
        IPageRepository pageRepository,
        ChapterStatus chapterStatus,
        List<Page> pages,
        List<Panel> panels,
        List<ScriptLine> existingLines,
        IReadOnlyList<ScriptTreePage> payloadPages,
        HashSet<Guid> touchedPanelIds,
        Guid userId,
        List<string> warnings,
        CancellationToken ct)
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

        if (chapterStatus == ChapterStatus.InProduction)
        {
            var blockedPageIds = new HashSet<Guid>(pagesWithArtwork);
            var blockedPanelIds = new HashSet<Guid>(panelsWithArtwork);

            var allAffectedPageIds = droppedPageIds
                .Concat(droppedPanels.Select(p => p.PageId))
                .Distinct()
                .ToList();

            var startedPageIds = (await db.StageTasks.AsNoTracking()
                    .Where(t => t.PageId != null && allAffectedPageIds.Contains(t.PageId.Value) &&
                                !NotStartedTaskStatuses.Contains(t.Status))
                    .Select(t => t.PageId!.Value)
                    .Distinct()
                    .ToListAsync(ct))
                .ToHashSet();

            foreach (var p in droppedPages.Where(p => startedPageIds.Contains(p.Id)))
                blockedPageIds.Add(p.Id);
            foreach (var panel in droppedPanels.Where(p => !droppedPageIds.Contains(p.PageId) && startedPageIds.Contains(p.PageId)))
                blockedPanelIds.Add(panel.Id);

            var allTargets = droppedPageIds.Concat(droppedPanelIds).ToList();
            if (allTargets.Count > 0)
            {
                var legacyAssignedTargets = await db.Assignments.AsNoTracking()
                    .Where(a => a.EntityType != WorkflowEntityType.Chapter && allTargets.Contains(a.TargetEntityId))
                    .Select(a => a.TargetEntityId)
                    .ToListAsync(ct);
                foreach (var id in legacyAssignedTargets)
                {
                    if (droppedPageIds.Contains(id)) blockedPageIds.Add(id);
                    if (droppedPanelIds.Contains(id)) blockedPanelIds.Add(id);
                }
            }

            var lineIdsByPanel = existingLines
                .Where(l => l.PanelId.HasValue)
                .GroupBy(l => l.PanelId!.Value)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToHashSet());

            foreach (var pageId in allAffectedPageIds)
            {
                var pageStream = await pageRepository.GetAsync(pageId, ct);
                if (pageStream is null) continue;
                var activeElements = pageStream.Elements.Values.Where(e => !e.IsRemoved).ToList();
                if (activeElements.Count == 0) continue;

                if (droppedPageIds.Contains(pageId) &&
                    activeElements.Any(e => e.ScriptLineId.HasValue || Page.TextElementTypes.Contains(e.ElementType) || !string.IsNullOrEmpty(e.AssetId)))
                {
                    blockedPageIds.Add(pageId);
                }

                foreach (var panel in droppedPanels.Where(p => p.PageId == pageId && !droppedPageIds.Contains(pageId)))
                {
                    var panelLineIds = lineIdsByPanel.GetValueOrDefault(panel.Id);
                    var hasBalloonOnPanel = activeElements.Any(e =>
                        e.PanelId == panel.Id ||
                        (e.ScriptLineId.HasValue && panelLineIds != null && panelLineIds.Contains(e.ScriptLineId.Value)));
                    if (hasBalloonOnPanel)
                        blockedPanelIds.Add(panel.Id);
                }
            }

            if (blockedPageIds.Count > 0 || blockedPanelIds.Count > 0)
            {
                var pageNumById = pages.ToDictionary(p => p.Id, p => p.PageNumber);
                var blockedPageNumbers = blockedPageIds
                    .Where(pageNumById.ContainsKey)
                    .Select(id => pageNumById[id])
                    .OrderBy(n => n)
                    .ToList();
                var label = blockedPageNumbers.Count > 0
                    ? $"Trang {string.Join(", ", blockedPageNumbers)}"
                    : "khung tranh";
                throw new ScriptSaveAbortException(
                    $"Không thể xóa {label} trong giai đoạn sản xuất vì đã có bản vẽ, bóng thoại hoặc công đoạn đang thực hiện.",
                    ResultErrorCodes.CannotDeleteActiveProductionPage,
                    new
                    {
                        blockedPageIds = blockedPageIds.ToList(),
                        blockedPanelIds = blockedPanelIds.ToList()
                    });
            }

            await ScriptStructureRules.RemoveAsync(db, pageRepository, droppedPages, droppedPanels, userId, ct);
            return;
        }

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
                    if (line.Kind == ScriptLineKind.Thought && string.IsNullOrWhiteSpace(line.SpeakerLabel))
                        throw Invalid($"{label}: dòng suy nghĩ (Thought) bắt buộc phải có nhân vật phát biểu.");
                    if (ScriptBibleRefs.ValidateKind(line.Refs, line.Kind, label) is { } kindError)
                        throw Invalid(kindError);
                }
            }
        }

        if (await ScriptStructureRules.ValidateCastRevisionsAsync(db, chapterId, casts, ct) is { } castError)
            throw Invalid(castError);
    }
}
