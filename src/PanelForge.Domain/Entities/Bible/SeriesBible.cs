using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Auth;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Bible;

/// <summary>
/// Aggregate Root cho Series Bible.
/// Kho tri thức cấu trúc và phiên bản theo từng chương gồm Characters, Locations, Props, Terminology, StyleRules, PlotFacts.
/// Dùng làm tài liệu quy chuẩn cho đội ngũ sáng tác và dữ liệu nền tảng (grounding source) cho AI consistency check.
/// </summary>
public class SeriesBible : BaseEntity
{
    public Guid SeriesId { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public int Version { get; private set; } = 1;

    public Series Series { get; private set; } = default!;
    public StudioWorkspace Workspace { get; private set; } = default!;
    public ICollection<BibleEntry> Entries { get; private set; } = [];

    // Helper queries cho 6 nhóm thực thể cốt lõi
    public IEnumerable<BibleEntry> Characters =>
        Entries.Where(e => e.Category == BibleEntryCategory.Character && e.IsActive);

    public IEnumerable<BibleEntry> Locations =>
        Entries.Where(e => e.Category == BibleEntryCategory.Location && e.IsActive);

    public IEnumerable<BibleEntry> Props =>
        Entries.Where(e => e.Category == BibleEntryCategory.Prop && e.IsActive);

    public IEnumerable<BibleEntry> Terminology =>
        Entries.Where(e => e.Category == BibleEntryCategory.Terminology && e.IsActive);

    public IEnumerable<BibleEntry> StyleRules =>
        Entries.Where(e => e.Category == BibleEntryCategory.StyleRule && e.IsActive);

    public IEnumerable<BibleEntry> PlotFacts =>
        Entries.Where(e => e.Category == BibleEntryCategory.PlotFact && e.IsActive);

    private SeriesBible() { }

    public static SeriesBible Create(
        Guid seriesId,
        Guid workspaceId,
        string name,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new SeriesBible
        {
            SeriesId = seriesId,
            WorkspaceId = workspaceId,
            Name = name.Trim(),
            Description = description?.Trim(),
            Version = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void UpdateDetails(string name, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Description = description?.Trim();
        Version++;
        UpdatedAt = DateTime.UtcNow;
    }

    public BibleEntry AddEntry(
        BibleEntryCategory category,
        string code,
        string name,
        string description,
        string? subtitle = null,
        string? detailsJson = null,
        string? referenceImageUrl = null,
        BiblePriority priority = BiblePriority.Standard,
        bool strictCheck = true)
    {
        var entry = BibleEntry.Create(
            Id,
            category,
            code,
            name,
            description,
            subtitle,
            detailsJson,
            referenceImageUrl,
            priority,
            strictCheck);

        Entries.Add(entry);
        UpdatedAt = DateTime.UtcNow;
        return entry;
    }
}
