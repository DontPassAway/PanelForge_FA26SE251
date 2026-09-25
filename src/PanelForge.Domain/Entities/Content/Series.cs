using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Auth;
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
    public ReadingDirection ReadingDirection { get; private set; }
    public string Genre { get; private set; } = "Action";
    public string Format { get; private set; } = "Manga";
    public string? ReleaseScheduleJson { get; private set; }

    public StudioWorkspace Workspace { get; private set; } = default!;
    public SeriesBible? Bible { get; private set; }
    public PipelineDefinition? PipelineDefinition { get; private set; }
    public SeriesPreset? Preset { get; private set; }
    public ICollection<Chapter> Chapters { get; private set; } = [];

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

    public void UpdateDetails(
        string title,
        string? synopsis,
        ReadingDirection readingDirection,
        string? genre = null,
        string? format = null,
        string? releaseScheduleJson = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Title = title.Trim();
        Synopsis = synopsis?.Trim();
        ReadingDirection = readingDirection;
        if (!string.IsNullOrWhiteSpace(genre)) Genre = genre.Trim();
        if (!string.IsNullOrWhiteSpace(format)) Format = format.Trim();
        if (releaseScheduleJson != null) ReleaseScheduleJson = releaseScheduleJson.Trim();
    }

    public void AttachBible(SeriesBible bible)
    {
        ArgumentNullException.ThrowIfNull(bible);
        Bible = bible;
    }

    public void SetPipelineDefinition(Guid? pipelineDefinitionId) => PipelineDefinitionId = pipelineDefinitionId;

    public void AttachPreset(SeriesPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        Preset = preset;
    }
}
