using Microsoft.EntityFrameworkCore;
using PanelForge.Domain.Entities.Auth;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;

namespace PanelForge.Application.Interfaces.Persistence;

public interface IPanelForgeDbContext
{
    DbSet<User> Users { get; }
    DbSet<StudioWorkspace> StudioWorkspaces { get; }
    DbSet<WorkspaceMember> WorkspaceMembers { get; }
    DbSet<ExternalPreviewLink> ExternalPreviewLinks { get; }

    DbSet<Series> Series { get; }
    DbSet<SeriesBible> SeriesBibles { get; }
    DbSet<BibleEntry> BibleEntries { get; }
    DbSet<BibleEntryRevision> BibleEntryRevisions { get; }
    DbSet<PipelineDefinition> PipelineDefinitions { get; }
    DbSet<PipelineStage> PipelineStages { get; }
    DbSet<StageTransition> StageTransitions { get; }
    DbSet<WorkflowTransitionLog> WorkflowTransitionLogs { get; }
    DbSet<Chapter> Chapters { get; }
    DbSet<Scene> Scenes { get; }
    DbSet<ScriptLine> ScriptLines { get; }
    DbSet<Page> Pages { get; }
    DbSet<Panel> Panels { get; }
    DbSet<Element> Elements { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
