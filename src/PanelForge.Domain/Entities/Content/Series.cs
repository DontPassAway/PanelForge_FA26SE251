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
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public StudioWorkspace Workspace { get; private set; } = default!;
    public SeriesBible? Bible { get; private set; }
    public PipelineDefinition? PipelineDefinition { get; private set; }
    public ICollection<Chapter> Chapters { get; private set; } = [];

    private Series() { }

    public static Series Create(
        Guid workspaceId,
        string title,
        ReadingDirection readingDirection,
        string? synopsis = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new Series
        {
            WorkspaceId = workspaceId,
            Title = title.Trim(),
            Synopsis = synopsis?.Trim(),
            ReadingDirection = readingDirection
        };
    }

    public void UpdateDetails(string title, string? synopsis, ReadingDirection readingDirection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Title = title.Trim();
        Synopsis = synopsis?.Trim();
        ReadingDirection = readingDirection;
    }

    public void AttachBible(SeriesBible bible)
    {
        ArgumentNullException.ThrowIfNull(bible);
        Bible = bible;
    }

    public void SetPipelineDefinition(Guid? pipelineDefinitionId) => PipelineDefinitionId = pipelineDefinitionId;
}
