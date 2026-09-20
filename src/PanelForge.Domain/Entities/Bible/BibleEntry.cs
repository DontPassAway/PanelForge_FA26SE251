using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Bible;

/// <summary>
/// Đại diện cho một thực thể cấu trúc trong Series Bible (Nhân vật, Địa điểm, Đạo cụ, Thuật ngữ, Quy tắc phong cách, Sự kiện cốt truyện).
/// </summary>
public class BibleEntry : BaseEntity
{
    public Guid SeriesBibleId { get; private set; }
    public BibleEntryCategory Category { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Subtitle { get; private set; }
    public string Description { get; private set; } = default!;
    public string DetailsJson { get; private set; } = "{}";
    public string? ReferenceImageUrl { get; private set; }
    public BiblePriority Priority { get; private set; } = BiblePriority.Standard;
    public bool StrictCheck { get; private set; } = true;
    public bool IsActive { get; private set; } = true;

    public SeriesBible SeriesBible { get; private set; } = default!;
    public ICollection<BibleEntryRevision> Revisions { get; private set; } = [];

    private BibleEntry() { }

    public static BibleEntry Create(
        Guid seriesBibleId,
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
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        return new BibleEntry
        {
            SeriesBibleId = seriesBibleId,
            Category = category,
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Subtitle = subtitle?.Trim(),
            Description = description.Trim(),
            DetailsJson = string.IsNullOrWhiteSpace(detailsJson) ? "{}" : detailsJson.Trim(),
            ReferenceImageUrl = referenceImageUrl?.Trim(),
            Priority = priority,
            StrictCheck = strictCheck,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void UpdateDetails(
        string name,
        string description,
        string? subtitle,
        string? detailsJson,
        string? referenceImageUrl,
        BiblePriority priority,
        bool strictCheck)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Name = name.Trim();
        Description = description.Trim();
        Subtitle = subtitle?.Trim();
        DetailsJson = string.IsNullOrWhiteSpace(detailsJson) ? "{}" : detailsJson.Trim();
        ReferenceImageUrl = referenceImageUrl?.Trim();
        Priority = priority;
        StrictCheck = strictCheck;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetActiveStatus(bool isActive)
    {
        IsActive = isActive;
        UpdatedAt = DateTime.UtcNow;
    }

    public void AddRevision(
        string summary,
        string snapshotJson,
        string contentHash,
        Guid? authorId = null,
        Guid? associatedChapterId = null)
    {
        var nextVersion = (Revisions.Count > 0) ? Revisions.Max(r => r.VersionNumber) + 1 : 1;
        var revision = BibleEntryRevision.Create(
            Id,
            nextVersion,
            summary,
            snapshotJson,
            contentHash,
            authorId,
            associatedChapterId);

        Revisions.Add(revision);
        UpdatedAt = DateTime.UtcNow;
    }
}
