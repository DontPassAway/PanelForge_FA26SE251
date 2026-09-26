using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Auth;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Content;

public class Chapter : BaseEntity
{
    public Guid SeriesId { get; private set; }
    public decimal ChapterNumber { get; private set; }
    public string? Title { get; private set; }
    public DateOnly? TargetReleaseDate { get; private set; }
    public long VersionVector { get; private set; } = 0;

    /// <summary>E-02: chuyển trạng thái do CF5 đảm nhiệm; mặc định InProduction.</summary>
    public ChapterStatus Status { get; private set; } = ChapterStatus.InProduction;
    public DateTime? PublishedAt { get; private set; }

    public Series Series { get; private set; } = default!;
    public ICollection<Scene> Scenes { get; private set; } = [];
    public ICollection<Page> Pages { get; private set; } = [];
    public ICollection<ExternalPreviewLink> ExternalPreviewLinks { get; private set; } = [];

    private Chapter() { }

    public static Chapter Create(
        Guid seriesId,
        decimal chapterNumber,
        string? title = null,
        DateOnly? targetReleaseDate = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chapterNumber);

        return new Chapter
        {
            SeriesId = seriesId,
            ChapterNumber = chapterNumber,
            Title = title?.Trim(),
            TargetReleaseDate = targetReleaseDate
        };
    }

    public void UpdateDetails(string? title, DateOnly? targetReleaseDate)
    {
        Title = title?.Trim();
        TargetReleaseDate = targetReleaseDate;
    }

    public void IncrementVersion() => VersionVector++;
}
