using PanelForge.Application.Features.ScriptVersions;

namespace PanelForge.Application.Features.Chapters.Services;

public readonly record struct ScriptPanelReference(int PageNumber, int PanelNumber, Guid? PanelId);

public sealed record ScriptImpactClassification(
    IReadOnlySet<ScriptPanelReference> VisualPanels,
    IReadOnlySet<Guid> StaleScriptLineIds)
{
    public bool HasVisualImpact => VisualPanels.Count > 0;
    public bool HasTextImpact => StaleScriptLineIds.Count > 0;
}

/// <summary>
/// Pure, deterministic classifier for a frozen script snapshot and its replacement.
/// It deliberately does not write workflow state: callers decide when and where to propagate each signal.
/// </summary>
public interface IScriptImpactClassifier
{
    ScriptImpactClassification Classify(
        ScriptVersionSnapshot oldScriptSnapshot,
        ScriptVersionSnapshot newScriptSnapshot);
}

public sealed class ScriptImpactClassifier : IScriptImpactClassifier
{
    private static readonly HashSet<string> TextKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dialogue", "Thought", "Narration", "Sfx"
    };

    public ScriptImpactClassification Classify(
        ScriptVersionSnapshot oldScriptSnapshot,
        ScriptVersionSnapshot newScriptSnapshot)
    {
        ArgumentNullException.ThrowIfNull(oldScriptSnapshot);
        ArgumentNullException.ThrowIfNull(newScriptSnapshot);

        var visualPanels = new HashSet<ScriptPanelReference>();
        var staleLineIds = new HashSet<Guid>();
        var oldRows = oldScriptSnapshot.Rows.ToDictionary(r => r.Key, StringComparer.Ordinal);
        var newRows = newScriptSnapshot.Rows.ToDictionary(r => r.Key, StringComparer.Ordinal);

        foreach (var key in oldRows.Keys.Union(newRows.Keys, StringComparer.Ordinal))
        {
            oldRows.TryGetValue(key, out var oldRow);
            newRows.TryGetValue(key, out var newRow);

            if (IsPanel(oldRow) || IsPanel(newRow))
            {
                if (PanelChanged(oldRow, newRow))
                {
                    AddPanel(visualPanels, oldRow);
                    AddPanel(visualPanels, newRow);
                }
                continue;
            }

            if (!IsLine(oldRow) && !IsLine(newRow)) continue;

            var oldIsAction = IsKind(oldRow, "Action");
            var newIsAction = IsKind(newRow, "Action");
            if ((oldIsAction || newIsAction) && LineChanged(oldRow, newRow))
            {
                AddPanel(visualPanels, oldRow);
                AddPanel(visualPanels, newRow);
            }

            if (IsText(oldRow) && TextBindingChanged(oldRow!, newRow) && TryLineId(oldRow!, out var lineId))
                staleLineIds.Add(lineId);
        }

        AddChangedChapterBindings(oldScriptSnapshot, newScriptSnapshot, visualPanels);
        return new ScriptImpactClassification(visualPanels, staleLineIds);
    }

    private static bool IsPanel(ScriptVersionRow? row) =>
        string.Equals(row?.Type, "panel", StringComparison.OrdinalIgnoreCase);

    private static bool IsLine(ScriptVersionRow? row) => row?.Key.StartsWith("line:", StringComparison.Ordinal) == true;

    private static bool IsText(ScriptVersionRow? row) => row is not null && TextKinds.Contains(row.Type);

    private static bool IsKind(ScriptVersionRow? row, string kind) =>
        string.Equals(row?.Type, kind, StringComparison.OrdinalIgnoreCase);

    private static bool PanelChanged(ScriptVersionRow? oldRow, ScriptVersionRow? newRow)
    {
        if (oldRow is null || newRow is null) return true;
        if (!string.Equals(oldRow.Text, newRow.Text, StringComparison.Ordinal)) return true;
        if (!SameCasts(oldRow.CastRevisions, newRow.CastRevisions)) return true;
        if (oldRow.EntityId.HasValue && newRow.EntityId.HasValue && oldRow.EntityId != newRow.EntityId) return true;
        return oldRow.BoundingBox is not null && newRow.BoundingBox is not null &&
               !string.Equals(oldRow.BoundingBox, newRow.BoundingBox, StringComparison.Ordinal);
    }

    private static bool LineChanged(ScriptVersionRow? oldRow, ScriptVersionRow? newRow)
    {
        if (oldRow is null || newRow is null) return true;
        return !string.Equals(oldRow.Type, newRow.Type, StringComparison.OrdinalIgnoreCase) ||
               oldRow.Page != newRow.Page || oldRow.Panel != newRow.Panel ||
               !string.Equals(oldRow.Text, newRow.Text, StringComparison.Ordinal) ||
               !string.Equals(oldRow.Label, newRow.Label, StringComparison.Ordinal);
    }

    private static bool TextBindingChanged(ScriptVersionRow oldRow, ScriptVersionRow? newRow) =>
        newRow is null || !IsText(newRow) ||
        !string.Equals(oldRow.Type, newRow.Type, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(oldRow.Text, newRow.Text, StringComparison.Ordinal) ||
        !string.Equals(oldRow.Label, newRow.Label, StringComparison.Ordinal);

    private static bool SameCasts(
        IReadOnlyList<ScriptVersionRowCast>? left,
        IReadOnlyList<ScriptVersionRowCast>? right) =>
        (left ?? []).OrderBy(x => x.EntryId).ThenBy(x => x.RevisionId)
        .SequenceEqual((right ?? []).OrderBy(x => x.EntryId).ThenBy(x => x.RevisionId));

    private static void AddChangedChapterBindings(
        ScriptVersionSnapshot oldSnapshot,
        ScriptVersionSnapshot newSnapshot,
        HashSet<ScriptPanelReference> visualPanels)
    {
        var oldChoices = (oldSnapshot.CastChoices ?? []).ToDictionary(x => x.BibleEntryId, x => x.RevisionId);
        var newChoices = (newSnapshot.CastChoices ?? []).ToDictionary(x => x.BibleEntryId, x => x.RevisionId);
        var changedEntries = oldChoices.Keys.Union(newChoices.Keys)
            .Where(id => oldChoices.GetValueOrDefault(id) != newChoices.GetValueOrDefault(id))
            .ToHashSet();
        if (changedEntries.Count == 0) return;

        var matchingRows = oldSnapshot.Rows.Concat(newSnapshot.Rows)
            .Where(row => (row.Refs?.Any(x => changedEntries.Contains(x.EntryId)) ?? false) ||
                          (row.CastRevisions?.Any(x => changedEntries.Contains(x.EntryId)) ?? false))
            .ToList();
        foreach (var row in matchingRows) AddPanel(visualPanels, row);

        // Older snapshots may not contain line-to-Bible references. Conservatively flag the chapter's panels.
        if (matchingRows.Count == 0)
        {
            foreach (var row in oldSnapshot.Rows.Concat(newSnapshot.Rows).Where(IsPanel))
                AddPanel(visualPanels, row);
        }
    }

    private static void AddPanel(HashSet<ScriptPanelReference> panels, ScriptVersionRow? row)
    {
        if (row is null || row.Page <= 0 || row.Panel <= 0) return;
        panels.Add(new ScriptPanelReference(row.Page, row.Panel, IsPanel(row) ? row.EntityId : null));
    }

    private static bool TryLineId(ScriptVersionRow row, out Guid lineId)
    {
        if (row.EntityId is { } entityId)
        {
            lineId = entityId;
            return true;
        }
        return Guid.TryParse(row.Key.AsSpan("line:".Length), out lineId);
    }
}
