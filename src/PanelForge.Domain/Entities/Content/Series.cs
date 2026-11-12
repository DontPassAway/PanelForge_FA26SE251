using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Content;

public class Series : BaseEntity
{
    public Guid WorkspaceId { get; private set; }
    public Guid? PipelineDefinitionId { get; private set; }
    public string Title { get; private set; } = default!;
    public string? Synopsis { get; private set; }
    /// <summary>V1.2: story arc của Series (Producer/Writer ghi), đưa vào input Writer Agent.</summary>
    public string? StoryArc { get; private set; }
    public ReadingDirection ReadingDirection { get; private set; }
    public string Genre { get; private set; } = "Action";
    public string Format { get; private set; } = "Manga";
    public string? ReleaseScheduleJson { get; private set; }
    /// <summary>SHA-256 ảnh bìa hiện tại (file trên kho asset theo hash); null = chưa có bìa.</summary>
    public string? CoverImageSha256 { get; private set; }
    /// <summary>SHA-256 avatar vuông của Series (nhận diện trong danh sách/breadcrumb); độc lập với bìa dọc.</summary>
    public string? AvatarImageSha256 { get; private set; }
    /// <summary>SHA-256 banner ngang (~3:1) đầu Series Dashboard; độc lập với bìa dọc và avatar.</summary>
    public string? BannerImageSha256 { get; private set; }
    /// <summary>Producer đánh dấu bộ truyện đã hoàn thành (Reader thấy "Hoàn thành" thay vì "Đang ra").</summary>
    public bool IsCompleted { get; private set; }

    public StudioWorkspace Workspace { get; private set; } = default!;
    public SeriesBible? Bible { get; private set; }
    public PipelineDefinition? PipelineDefinition { get; private set; }

    /// <summary>Task 8: Series-level typography presets (replaces SeriesPreset.TypographyPresetsJson).</summary>
    public ICollection<TypographyPreset> TypographyPresets { get; private set; } = [];

    /// <summary>Task 9: Series-level consistency rules (replaces SeriesPreset.ConsistencyRulesJson).</summary>
    public ICollection<ConsistencyRule> ConsistencyRules { get; private set; } = [];

    public ICollection<Chapter> Chapters { get; private set; } = [];
    public ICollection<SeriesMember> Members { get; private set; } = [];

    private Series() { }

    public static Series Create(
        Guid workspaceId,
        string title,
        ReadingDirection readingDirection,
        string? synopsis = null,
        string genre = "Action",
        string format = "Manga",
        string? releaseScheduleJson = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new Series
        {
            WorkspaceId = workspaceId,
            Title = title.Trim(),
            Synopsis = synopsis?.Trim(),
            ReadingDirection = readingDirection,
            Genre = string.IsNullOrWhiteSpace(genre) ? "Action" : genre.Trim(),
            Format = string.IsNullOrWhiteSpace(format) ? "Manga" : format.Trim(),
            ReleaseScheduleJson = releaseScheduleJson?.Trim()
        };
    }

    public void SetCompleted(bool isCompleted)
    {
        if (IsCompleted == isCompleted) return;
        IsCompleted = isCompleted;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateDetails(
        string title,
        string? synopsis,
        ReadingDirection readingDirection,
        string? genre = null,
        string? format = null,
        string? releaseScheduleJson = null,
        string? storyArc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Title = title.Trim();
        if (storyArc != null) StoryArc = string.IsNullOrWhiteSpace(storyArc) ? null : storyArc.Trim();
        Synopsis = synopsis?.Trim();
        ReadingDirection = readingDirection;
        if (!string.IsNullOrWhiteSpace(genre)) Genre = genre.Trim();
        if (!string.IsNullOrWhiteSpace(format)) Format = format.Trim();
        if (releaseScheduleJson != null) ReleaseScheduleJson = releaseScheduleJson.Trim();
    }

    /// <summary>Producer/Writer ghi story arc của Series; chuỗi rỗng = xoá.</summary>
    public void SetStoryArc(string? storyArc) => StoryArc = string.IsNullOrWhiteSpace(storyArc) ? null : storyArc.Trim();

    /// <summary>Producer đặt / xoá ảnh bìa (null = xoá).</summary>
    public void SetCover(string? sha256) => CoverImageSha256 = sha256;

    /// <summary>Producer đặt / xoá avatar Series (null = xoá).</summary>
    public void SetAvatar(string? sha256) => AvatarImageSha256 = sha256;

    /// <summary>Producer đặt / xoá banner Series (null = xoá).</summary>
    public void SetBanner(string? sha256) => BannerImageSha256 = sha256;

    public void AttachBible(SeriesBible bible)
    {
        ArgumentNullException.ThrowIfNull(bible);
        Bible = bible;
    }

    public void SetPipelineDefinition(Guid? pipelineDefinitionId) => PipelineDefinitionId = pipelineDefinitionId;
}
