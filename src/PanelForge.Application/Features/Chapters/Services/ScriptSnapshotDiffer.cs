using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Application.Features.ScriptVersions;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Services;

internal sealed record ScriptDiffAnalysis(
    IReadOnlyList<RefreezeAddedPageDto> AddedPages,
    IReadOnlyList<RefreezeRemovedPageDto> RemovedPages,
    IReadOnlyList<RefreezeVisualImpactDto> VisualImpacts,
    IReadOnlyList<RefreezeTextImpactDto> TextImpacts,
    IReadOnlySet<Guid> VisuallyImpactedPageIds,
    IReadOnlySet<Guid> VisuallyImpactedPanelIds,
    IReadOnlyDictionary<Guid, Guid> VisualImpactLineByPanel,
    IReadOnlySet<Guid> VisualImpactArtistUserIds,
    IReadOnlySet<Guid> TextImpactLettererUserIds);

internal static class ScriptSnapshotDiffer
{
    private static readonly HashSet<string> TextRowTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(ScriptLineKind.Dialogue),
        nameof(ScriptLineKind.Thought),
        nameof(ScriptLineKind.Narration),
        nameof(ScriptLineKind.Sfx)
    };

    private static readonly StageTaskStatus[] NotStartedStatuses =
        [StageTaskStatus.Cancelled, StageTaskStatus.Unassigned, StageTaskStatus.Assigned];

    public static async Task<ScriptDiffAnalysis> ComputeAsync(
        IPanelForgeDbContext db,
        IPageRepository pageRepository,
        Chapter chapter,
        ScriptVersionSnapshot oldSnapshot,
        ScriptVersionSnapshot newSnapshot,
        CancellationToken ct)
    {
        var activePages = await db.Pages.AsNoTracking()
            .Where(p => p.ChapterId == chapter.Id)
            .OrderBy(p => p.PageNumber)
            .ToListAsync(ct);
        var activePageByNumber = activePages.ToDictionary(p => p.PageNumber);
        var activePageIds = activePages.Select(p => p.Id).ToList();

        var allPagesWithDeleted = await db.Pages.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.ChapterId == chapter.Id)
            .ToListAsync(ct);

        var activePanels = activePageIds.Count == 0
            ? []
            : await db.Panels.AsNoTracking()
                .Where(p => activePageIds.Contains(p.PageId))
                .ToListAsync(ct);
        var panelByPageAndNumber = activePanels.ToDictionary(p => (p.PageId, p.PanelNumber));

        var stageTasks = await db.StageTasks.AsNoTracking()
            .Where(t => t.ChapterId == chapter.Id && t.PageId != null && t.Status != StageTaskStatus.Cancelled)
            .ToListAsync(ct);
        var stagesById = await db.PipelineStages.AsNoTracking()
            .Where(s => stageTasks.Select(t => t.PipelineStageId).Distinct().Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);
        var lotsById = await db.StageTaskLots.AsNoTracking()
            .Where(l => l.ChapterId == chapter.Id)
            .ToDictionaryAsync(l => l.Id, ct);
        var assigneeUserIds = lotsById.Values.Select(l => l.AssigneeUserId).Distinct().ToList();
        var userNames = assigneeUserIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Users.AsNoTracking()
                .Where(u => assigneeUserIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        // Suggest primary Artist & Letterer from most frequent existing assignments in the chapter
        var (suggestedArtistId, suggestedLettererId) = ResolveSuggestedAssignees(stageTasks, stagesById, lotsById);

        var oldPageNumbers = oldSnapshot.Rows.Where(r => r.Type == "page").Select(r => r.Page).ToHashSet();
        var newPageNumbers = newSnapshot.Rows.Where(r => r.Type == "page").Select(r => r.Page).ToHashSet();
        var hasBaseline = oldPageNumbers.Count > 0;

        var tasksByPageId = stageTasks.GroupBy(t => t.PageId!.Value).ToDictionary(g => g.Key, g => g.ToList());

        // 1. Added Pages: present in new snapshot and either absent in old snapshot or has zero grid tasks yet
        var addedPages = new List<RefreezeAddedPageDto>();
        foreach (var page in activePages.Where(p => newPageNumbers.Contains(p.PageNumber)))
        {
            var pageTasks = tasksByPageId.GetValueOrDefault(page.Id, []);
            var isNew = hasBaseline ? !oldPageNumbers.Contains(page.PageNumber) : pageTasks.Count == 0;
            if (!isNew) continue;

            var panelCount = activePanels.Count(p => p.PageId == page.Id);
            addedPages.Add(new RefreezeAddedPageDto(
                page.Id,
                page.PageNumber,
                panelCount,
                suggestedArtistId,
                suggestedArtistId.HasValue ? userNames.GetValueOrDefault(suggestedArtistId.Value) : null,
                suggestedLettererId,
                suggestedLettererId.HasValue ? userNames.GetValueOrDefault(suggestedLettererId.Value) : null));
        }

        // 2. Removed Pages: in old snapshot (or still having open StageTasks) but missing in new snapshot
        var candidateRemovedPages = allPagesWithDeleted
            .Where(p => !newPageNumbers.Contains(p.PageNumber) &&
                        ((hasBaseline && oldPageNumbers.Contains(p.PageNumber)) || tasksByPageId.ContainsKey(p.Id)))
            .GroupBy(p => p.Id)
            .Select(g => g.First())
            .OrderBy(p => p.PageNumber)
            .ToList();

        var removedPages = new List<RefreezeRemovedPageDto>();
        if (candidateRemovedPages.Count > 0)
        {
            var removedPageIds = candidateRemovedPages.Select(p => p.Id).ToList();
            var artworkPageIds = (await db.AssetReferences.AsNoTracking()
                    .Where(r => removedPageIds.Contains(r.PageId))
                    .Select(r => r.PageId)
                    .Distinct()
                    .ToListAsync(ct))
                .ToHashSet();

            foreach (var rp in candidateRemovedPages)
            {
                var rpTasks = tasksByPageId.GetValueOrDefault(rp.Id, []);
                var activeTaskCount = rpTasks.Count(t => !NotStartedStatuses.Contains(t.Status));
                var hasArtwork = artworkPageIds.Contains(rp.Id);
                var pageStream = await pageRepository.GetAsync(rp.Id, ct);
                var hasBalloons = pageStream is not null && pageStream.Elements.Values
                    .Any(e => !e.IsRemoved && (e.ScriptLineId.HasValue || Page.TextElementTypes.Contains(e.ElementType) || !string.IsNullOrEmpty(e.AssetId)));

                removedPages.Add(new RefreezeRemovedPageDto(
                    rp.Id,
                    rp.PageNumber,
                    CanSafelyDelete: activeTaskCount == 0 && !hasArtwork && !hasBalloons,
                    ActiveTaskCount: activeTaskCount,
                    HasArtwork: hasArtwork,
                    HasBalloons: hasBalloons));
            }
        }

        // 3. Detect Bible revision changes between old and new snapshots
        var changedBibleEntryIds = ResolveChangedBibleEntryIds(oldSnapshot, newSnapshot);
        var changedEntryNames = await LoadEntryNamesAsync(db, changedBibleEntryIds, ct);

        // 4. Visual Impacts (only on existing pages whose Artist stage is past NotStarted)
        var visualImpacts = new List<RefreezeVisualImpactDto>();
        var visuallyImpactedPageIds = new HashSet<Guid>();
        var visuallyImpactedPanelIds = new HashSet<Guid>();
        var visualImpactLineByPanel = new Dictionary<Guid, Guid>();
        var visualImpactArtistUserIds = new HashSet<Guid>();

        var oldPanelsByPage = oldSnapshot.Rows
            .Where(r => r.Type == "panel")
            .GroupBy(r => r.Page)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.Panel));
        var newPanelsByPage = newSnapshot.Rows
            .Where(r => r.Type == "panel")
            .GroupBy(r => r.Page)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.Panel));

        var oldActionLinesByPanel = oldSnapshot.Rows
            .Where(r => r.Type.Equals(nameof(ScriptLineKind.Action), StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => (r.Page, r.Panel))
            .ToDictionary(g => g.Key, g => g.ToList());
        var newActionLinesByPanel = newSnapshot.Rows
            .Where(r => r.Type.Equals(nameof(ScriptLineKind.Action), StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => (r.Page, r.Panel))
            .ToDictionary(g => g.Key, g => g.ToList());

        var newLinesByPanel = newSnapshot.Rows
            .Where(r => r.Key.StartsWith("line:", StringComparison.Ordinal))
            .GroupBy(r => (r.Page, r.Panel))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var page in activePages.Where(p => hasBaseline && oldPageNumbers.Contains(p.PageNumber) && newPageNumbers.Contains(p.PageNumber)))
        {
            var pageTasks = tasksByPageId.GetValueOrDefault(page.Id, []);
            var startedArtistTasks = pageTasks
                .Where(t => stagesById.TryGetValue(t.PipelineStageId, out var s) &&
                            s.AllowedRole != WorkspaceRole.Letterer &&
                            !NotStartedStatuses.Contains(t.Status))
                .ToList();

            if (startedArtistTasks.Count == 0) continue;

            var stageNames = startedArtistTasks
                .Select(t => stagesById[t.PipelineStageId].Name)
                .Distinct()
                .ToList();
            var pageArtistIds = startedArtistTasks
                .Where(t => t.LotId.HasValue && lotsById.ContainsKey(t.LotId.Value))
                .Select(t => lotsById[t.LotId!.Value].AssigneeUserId)
                .Distinct()
                .ToList();
            var assigneeNames = pageArtistIds
                .Select(id => userNames.GetValueOrDefault(id, "Họa sĩ"))
                .Distinct()
                .ToList();

            var oldPanels = oldPanelsByPage.GetValueOrDefault(page.PageNumber, []);
            var newPanels = newPanelsByPage.GetValueOrDefault(page.PageNumber, []);
            var allPanelNums = oldPanels.Keys.Union(newPanels.Keys).OrderBy(n => n).ToList();

            foreach (var panelNum in allPanelNums)
            {
                panelByPageAndNumber.TryGetValue((page.Id, panelNum), out var panelEntity);
                var hasOld = oldPanels.TryGetValue(panelNum, out var oldPanel);
                var hasNew = newPanels.TryGetValue(panelNum, out var newPanel);

                if (!hasOld && hasNew)
                {
                    AddVisualImpact(page.Id, page.PageNumber, panelNum, panelEntity?.Id, "PanelAdded",
                        $"Thêm mới khung tranh {panelNum} trên trang đã bắt đầu vẽ.",
                        stageNames, assigneeNames, pageArtistIds, null);
                    continue;
                }

                if (hasOld && !hasNew)
                {
                    AddVisualImpact(page.Id, page.PageNumber, panelNum, null, "PanelDeleted",
                        $"Gỡ bỏ khung tranh {panelNum} khỏi trang đã bắt đầu vẽ.",
                        stageNames, assigneeNames, pageArtistIds, null);
                    continue;
                }

                if (oldPanel is null || newPanel is null) continue;

                if (!string.Equals(oldPanel.Text.Trim(), newPanel.Text.Trim(), StringComparison.Ordinal))
                {
                    AddVisualImpact(page.Id, page.PageNumber, panelNum, panelEntity?.Id, "DirectionChanged",
                        $"Hướng vẽ / bố cục khung tranh {panelNum} đã thay đổi.",
                        stageNames, assigneeNames, pageArtistIds, null);
                }

                if (!CastListsEqual(oldPanel.CastRevisions, newPanel.CastRevisions))
                {
                    AddVisualImpact(page.Id, page.PageNumber, panelNum, panelEntity?.Id, "VariantChanged",
                        $"Biến thể trang phục/nhân vật của khung tranh {panelNum} đã thay đổi.",
                        stageNames, assigneeNames, pageArtistIds, null);
                }

                var oldActions = oldActionLinesByPanel.GetValueOrDefault((page.PageNumber, panelNum), []);
                var newActions = newActionLinesByPanel.GetValueOrDefault((page.PageNumber, panelNum), []);
                if (!ActionLinesEqual(oldActions, newActions))
                {
                    var changedLineId = FindFirstChangedActionLineId(oldActions, newActions);
                    AddVisualImpact(page.Id, page.PageNumber, panelNum, panelEntity?.Id, "ActionEdited",
                        $"Mô tả hành động khung tranh {panelNum} đã được chỉnh sửa.",
                        stageNames, assigneeNames, pageArtistIds, changedLineId);
                }

                if (changedBibleEntryIds.Count > 0)
                {
                    var panelLines = newLinesByPanel.GetValueOrDefault((page.PageNumber, panelNum), []);
                    var referencesChangedBible =
                        (newPanel.CastRevisions?.Any(c => changedBibleEntryIds.Contains(c.EntryId)) ?? false) ||
                        panelLines.Any(l =>
                            (l.Refs?.Any(r => changedBibleEntryIds.Contains(r.EntryId)) ?? false) ||
                            (!string.IsNullOrWhiteSpace(l.Label) && changedEntryNames.Contains(SpeakerResolver.NormalizeSpeaker(l.Label))));

                    if (referencesChangedBible)
                    {
                        AddVisualImpact(page.Id, page.PageNumber, panelNum, panelEntity?.Id, "BibleRevisionChanged",
                            $"Thiết kế Series Bible của thực thể trong khung tranh {panelNum} đã thay đổi.",
                            stageNames, assigneeNames, pageArtistIds, null);
                    }
                }
            }
        }

        // 5. Text Impacts (Dialogue, Thought, Narration, Sfx) — NEVER marks Artist tasks outdated
        var textImpacts = new List<RefreezeTextImpactDto>();
        var textImpactLettererUserIds = new HashSet<Guid>();
        var oldTextLines = oldSnapshot.Rows
            .Where(r => r.Key.StartsWith("line:", StringComparison.Ordinal) && TextRowTypes.Contains(r.Type))
            .ToDictionary(r => r.Key);
        var newTextLines = newSnapshot.Rows
            .Where(r => r.Key.StartsWith("line:", StringComparison.Ordinal) && TextRowTypes.Contains(r.Type))
            .ToDictionary(r => r.Key);

        var candidateTextChanges = new List<(ScriptVersionRow? OldRow, ScriptVersionRow? NewRow)>();
        foreach (var (key, oldRow) in oldTextLines)
        {
            if (!newTextLines.TryGetValue(key, out var newRow))
            {
                candidateTextChanges.Add((oldRow, null));
            }
            else if (!string.Equals(oldRow.Text.Trim(), newRow.Text.Trim(), StringComparison.Ordinal) ||
                     !string.Equals(oldRow.Label?.Trim(), newRow.Label?.Trim(), StringComparison.Ordinal) ||
                     !string.Equals(oldRow.Type, newRow.Type, StringComparison.OrdinalIgnoreCase))
            {
                candidateTextChanges.Add((oldRow, newRow));
            }
        }

        if (candidateTextChanges.Count > 0)
        {
            var pageIdsForTextCheck = candidateTextChanges
                .Select(c => (c.NewRow ?? c.OldRow)!.Page)
                .Distinct()
                .Where(activePageByNumber.ContainsKey)
                .Select(pn => activePageByNumber[pn].Id)
                .Distinct()
                .ToList();

            var streamByPageId = new Dictionary<Guid, Page?>();
            foreach (var pid in pageIdsForTextCheck)
            {
                streamByPageId[pid] = await pageRepository.GetAsync(pid, ct);
            }

            foreach (var (oldRow, newRow) in candidateTextChanges)
            {
                var refRow = newRow ?? oldRow!;
                var lineId = ParseLineGuid(refRow.Key);
                activePageByNumber.TryGetValue(refRow.Page, out var pageEntity);
                var pageStream = pageEntity is not null ? streamByPageId.GetValueOrDefault(pageEntity.Id) : null;

                var boundBalloons = pageStream is not null && lineId.HasValue
                    ? pageStream.Elements.Values
                        .Where(e => !e.IsRemoved && e.ScriptLineId == lineId.Value)
                        .ToList()
                    : [];

                if (pageEntity is not null)
                {
                    foreach (var lt in tasksByPageId.GetValueOrDefault(pageEntity.Id, [])
                        .Where(t => stagesById.TryGetValue(t.PipelineStageId, out var s) &&
                                    s.AllowedRole == WorkspaceRole.Letterer &&
                                    t.LotId.HasValue && lotsById.ContainsKey(t.LotId.Value)))
                    {
                        textImpactLettererUserIds.Add(lotsById[lt.LotId!.Value].AssigneeUserId);
                    }
                }

                if (boundBalloons.Count > 0)
                {
                    foreach (var balloon in boundBalloons)
                    {
                        textImpacts.Add(new RefreezeTextImpactDto(
                            refRow.Page,
                            refRow.Panel,
                            lineId,
                            balloon.ElementId,
                            refRow.Type,
                            newRow?.Label ?? oldRow?.Label,
                            oldRow?.Text,
                            newRow?.Text,
                            IsStale: true));
                    }
                }
                else
                {
                    textImpacts.Add(new RefreezeTextImpactDto(
                        refRow.Page,
                        refRow.Panel,
                        lineId,
                        ElementId: null,
                        refRow.Type,
                        newRow?.Label ?? oldRow?.Label,
                        oldRow?.Text,
                        newRow?.Text,
                        IsStale: false));
                }
            }
        }

        return new ScriptDiffAnalysis(
            AddedPages: addedPages,
            RemovedPages: removedPages,
            VisualImpacts: visualImpacts,
            TextImpacts: textImpacts,
            VisuallyImpactedPageIds: visuallyImpactedPageIds,
            VisuallyImpactedPanelIds: visuallyImpactedPanelIds,
            VisualImpactLineByPanel: visualImpactLineByPanel,
            VisualImpactArtistUserIds: visualImpactArtistUserIds,
            TextImpactLettererUserIds: textImpactLettererUserIds);

        void AddVisualImpact(
            Guid pageId,
            int pageNumber,
            int panelNumber,
            Guid? panelId,
            string changeType,
            string reason,
            IReadOnlyList<string> stages,
            IReadOnlyList<string> assignees,
            IReadOnlyList<Guid> artistIds,
            Guid? lineId)
        {
            visuallyImpactedPageIds.Add(pageId);
            if (panelId.HasValue)
            {
                visuallyImpactedPanelIds.Add(panelId.Value);
                if (lineId.HasValue)
                    visualImpactLineByPanel.TryAdd(panelId.Value, lineId.Value);
            }
            foreach (var aid in artistIds)
                visualImpactArtistUserIds.Add(aid);

            visualImpacts.Add(new RefreezeVisualImpactDto(
                pageNumber, panelNumber, panelId, changeType, reason, stages, assignees));
        }
    }

    private static (Guid? ArtistId, Guid? LettererId) ResolveSuggestedAssignees(
        List<StageTask> stageTasks,
        Dictionary<Guid, PipelineStage> stagesById,
        Dictionary<Guid, StageTaskLot> lotsById)
    {
        var artistId = stageTasks
            .Where(t => t.LotId.HasValue && lotsById.ContainsKey(t.LotId.Value) &&
                        stagesById.TryGetValue(t.PipelineStageId, out var s) &&
                        s.AllowedRole == WorkspaceRole.Artist)
            .GroupBy(t => lotsById[t.LotId!.Value].AssigneeUserId)
            .OrderByDescending(g => g.Count())
            .Select(g => (Guid?)g.Key)
            .FirstOrDefault();

        var lettererId = stageTasks
            .Where(t => t.LotId.HasValue && lotsById.ContainsKey(t.LotId.Value) &&
                        stagesById.TryGetValue(t.PipelineStageId, out var s) &&
                        s.AllowedRole == WorkspaceRole.Letterer)
            .GroupBy(t => lotsById[t.LotId!.Value].AssigneeUserId)
            .OrderByDescending(g => g.Count())
            .Select(g => (Guid?)g.Key)
            .FirstOrDefault();

        return (artistId, lettererId);
    }

    private static HashSet<Guid> ResolveChangedBibleEntryIds(
        ScriptVersionSnapshot oldSnapshot,
        ScriptVersionSnapshot newSnapshot)
    {
        var changed = new HashSet<Guid>();
        var oldChoices = (oldSnapshot.CastChoices ?? []).ToDictionary(c => c.BibleEntryId, c => c.RevisionId);
        var newChoices = (newSnapshot.CastChoices ?? []).ToDictionary(c => c.BibleEntryId, c => c.RevisionId);

        foreach (var (entryId, revId) in newChoices)
        {
            if (!oldChoices.TryGetValue(entryId, out var prevRevId) || prevRevId != revId)
                changed.Add(entryId);
        }

        return changed;
    }

    private static async Task<HashSet<string>> LoadEntryNamesAsync(
        IPanelForgeDbContext db,
        HashSet<Guid> entryIds,
        CancellationToken ct)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (entryIds.Count == 0) return names;

        var entries = await db.BibleEntries.AsNoTracking()
            .Where(e => entryIds.Contains(e.Id))
            .Select(e => new { e.NormalizedName, e.Aliases })
            .ToListAsync(ct);

        foreach (var e in entries)
        {
            names.Add(e.NormalizedName);
            foreach (var alias in e.Aliases)
            {
                var norm = Domain.Common.BibleNameNormalizer.Normalize(alias);
                if (!string.IsNullOrEmpty(norm))
                    names.Add(norm);
            }
        }
        return names;
    }

    private static bool CastListsEqual(
        IReadOnlyList<ScriptVersionRowCast>? a,
        IReadOnlyList<ScriptVersionRowCast>? b)
    {
        var listA = (a ?? []).OrderBy(x => x.EntryId).Select(x => (x.EntryId, x.RevisionId)).ToList();
        var listB = (b ?? []).OrderBy(x => x.EntryId).Select(x => (x.EntryId, x.RevisionId)).ToList();
        return listA.SequenceEqual(listB);
    }

    private static bool ActionLinesEqual(List<ScriptVersionRow> a, List<ScriptVersionRow> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i].Key != b[i].Key || !string.Equals(a[i].Text.Trim(), b[i].Text.Trim(), StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private static Guid? FindFirstChangedActionLineId(List<ScriptVersionRow> oldLines, List<ScriptVersionRow> newLines)
    {
        var oldByKey = oldLines.ToDictionary(l => l.Key, l => l.Text.Trim());
        foreach (var nl in newLines)
        {
            if (!oldByKey.TryGetValue(nl.Key, out var oldText) || !string.Equals(oldText, nl.Text.Trim(), StringComparison.Ordinal))
                return ParseLineGuid(nl.Key);
        }
        return oldLines.Select(l => ParseLineGuid(l.Key)).FirstOrDefault(id => id.HasValue);
    }

    private static Guid? ParseLineGuid(string key) =>
        key.StartsWith("line:", StringComparison.Ordinal) && Guid.TryParse(key.AsSpan(5), out var id)
            ? id
            : null;
}
