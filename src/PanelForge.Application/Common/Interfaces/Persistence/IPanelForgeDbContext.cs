using Microsoft.EntityFrameworkCore;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Entities.Assets;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Domain.Entities.Publishing;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.MasterData;
using PanelForge.Domain.Entities.Workflow;

namespace PanelForge.Application.Common.Interfaces.Persistence;

public interface IPanelForgeDbContext
{
    DbSet<User> Users { get; }
    DbSet<StudioWorkspace> StudioWorkspaces { get; }
    DbSet<WorkspaceMember> WorkspaceMembers { get; }
    DbSet<ExternalPreviewLink> ExternalPreviewLinks { get; }
    DbSet<UserRememberedDevice> UserRememberedDevices { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<WorkspaceAiConfig> WorkspaceAiConfigs { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<AiUsageRecord> AiUsageRecords { get; }
    DbSet<AiWorkflowRun> AiWorkflowRuns { get; }
    DbSet<AiAgentStep> AiAgentSteps { get; }
    DbSet<AiSuggestion> AiSuggestions { get; }
    DbSet<AiSuggestionDecision> AiSuggestionDecisions { get; }
    DbSet<ContentRevision> ContentRevisions { get; }

    // Master Data
    DbSet<ElementTypeMasterData> ElementTypes { get; }
    DbSet<ExportPreset> ExportPresets { get; }
    DbSet<PipelineTemplate> PipelineTemplates { get; }
    DbSet<PipelineTemplateStage> PipelineTemplateStages { get; }

    DbSet<Series> Series { get; }
    DbSet<TypographyPreset> TypographyPresets { get; }
    DbSet<ConsistencyRule> ConsistencyRules { get; }
    DbSet<SeriesBible> SeriesBibles { get; }
    DbSet<BibleEntry> BibleEntries { get; }
    DbSet<BibleEntryRevision> BibleEntryRevisions { get; }
    DbSet<PipelineDefinition> PipelineDefinitions { get; }
    DbSet<PipelineStage> PipelineStages { get; }
    DbSet<StageTransition> StageTransitions { get; }
    DbSet<Assignment> Assignments { get; }
    DbSet<StageProgress> StageProgresses { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<Submission> Submissions { get; }
    DbSet<ChangeRequest> ChangeRequests { get; }
    DbSet<ConsistencyFinding> ConsistencyFindings { get; }
    DbSet<PanelForge.Domain.Entities.Preview.PreviewLink> PreviewLinks { get; }
    DbSet<PanelForge.Domain.Entities.Preview.PreviewLinkAccessLog> PreviewLinkAccessLogs { get; }
    DbSet<PanelForge.Domain.Entities.Preview.PreviewFeedback> PreviewFeedback { get; }
    DbSet<Asset> Assets { get; }
    DbSet<AssetReference> AssetReferences { get; }
    DbSet<AssetAccessLog> AssetAccessLogs { get; }
    DbSet<Chapter> Chapters { get; }
    DbSet<ChapterReview> ChapterReviews { get; }
    DbSet<Scene> Scenes { get; }
    DbSet<ScriptLine> ScriptLines { get; }
    DbSet<Page> Pages { get; }
    DbSet<Panel> Panels { get; }
    DbSet<FrameTemplate> FrameTemplates { get; }
    DbSet<ScriptLineBibleLink> ScriptLineBibleLinks { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
