using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using PanelForge.Application.Interfaces;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Auth;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.MasterData;
using PanelForge.Domain.Entities.Workflow;

namespace PanelForge.Infrastructure.Persistence;

public sealed class PanelForgeDbContext : DbContext, IPanelForgeDbContext
{
    private readonly ICurrentUserService? _currentUser;

    /// <param name="currentUser">Null khi chạy ngoài HTTP request (design-time, migration, seeder).</param>
    public PanelForgeDbContext(DbContextOptions<PanelForgeDbContext> options, ICurrentUserService? currentUser = null)
        : base(options)
    {
        _currentUser = currentUser;
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<StudioWorkspace> StudioWorkspaces => Set<StudioWorkspace>();
    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();
    public DbSet<ExternalPreviewLink> ExternalPreviewLinks => Set<ExternalPreviewLink>();
    public DbSet<UserRememberedDevice> UserRememberedDevices => Set<UserRememberedDevice>();
    public DbSet<WorkspaceAiConfig> WorkspaceAiConfigs => Set<WorkspaceAiConfig>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AiUsageRecord> AiUsageRecords => Set<AiUsageRecord>();

    // Master Data
    public DbSet<ElementTypeMasterData> ElementTypes => Set<ElementTypeMasterData>();
    public DbSet<ExportPreset> ExportPresets => Set<ExportPreset>();
    public DbSet<PipelineTemplate> PipelineTemplates => Set<PipelineTemplate>();
    public DbSet<PipelineTemplateStage> PipelineTemplateStages => Set<PipelineTemplateStage>();

    public DbSet<Series> Series => Set<Series>();
    public DbSet<TypographyPreset> TypographyPresets => Set<TypographyPreset>();
    public DbSet<ConsistencyRule> ConsistencyRules => Set<ConsistencyRule>();
    public DbSet<SeriesBible> SeriesBibles => Set<SeriesBible>();
    public DbSet<BibleEntry> BibleEntries => Set<BibleEntry>();
    public DbSet<BibleEntryRevision> BibleEntryRevisions => Set<BibleEntryRevision>();
    public DbSet<PipelineDefinition> PipelineDefinitions => Set<PipelineDefinition>();
    public DbSet<PipelineStage> PipelineStages => Set<PipelineStage>();
    public DbSet<StageTransition> StageTransitions => Set<StageTransition>();
    public DbSet<WorkflowTransitionLog> WorkflowTransitionLogs => Set<WorkflowTransitionLog>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<Chapter> Chapters => Set<Chapter>();
    public DbSet<Scene> Scenes => Set<Scene>();
    public DbSet<ScriptLine> ScriptLines => Set<ScriptLine>();
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<Panel> Panels => Set<Panel>();
    public DbSet<Element> Elements => Set<Element>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var actorId = _currentUser?.UserId?.ToString();

        // Chụp trạng thái TRƯỚC khi đổi Deleted → Modified (soft-delete), để audit ghi đúng hành động DELETE.
        var pending = new List<(EntityEntry Entry, string Action, string? ChangesJson)>();

        foreach (var entry in ChangeTracker.Entries().ToList())
        {
            if (entry.State is EntityState.Detached or EntityState.Unchanged)
                continue;

            if (entry.Entity is not (AuditLog or AiUsageRecord))
            {
                var action = entry.State switch
                {
                    EntityState.Added => "CREATE",
                    EntityState.Modified => "UPDATE",
                    EntityState.Deleted => "DELETE",
                    _ => entry.State.ToString().ToUpperInvariant()
                };
                pending.Add((entry, action, AuditChangeSerializer.Serialize(entry)));
            }

            // 1. Timestamps + người thực hiện (BR-18)
            if (entry.Entity is IAuditableEntity auditable)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        auditable.CreatedAt = now;
                        if (string.IsNullOrEmpty(auditable.CreatedBy)) auditable.CreatedBy = actorId;
                        break;
                    case EntityState.Modified:
                        auditable.UpdatedAt = now;
                        if (actorId is not null) auditable.UpdatedBy = actorId;
                        break;
                }
            }

            // 2. Chuyển xóa vật lý thành soft-delete — chỉ với entity thực sự map cột IsDeleted.
            //    Entity bỏ qua IsDeleted (Users, master data...) vẫn xóa vật lý như cấu hình của chúng.
            if (entry.Entity is ISoftDelete softDelete
                && entry.State == EntityState.Deleted
                && entry.Metadata.FindProperty(nameof(ISoftDelete.IsDeleted)) is not null)
            {
                entry.State = EntityState.Modified;
                softDelete.IsDeleted = true;
                softDelete.DeletedAt = now;
                softDelete.DeletedBy = actorId;

                if (entry.Entity is IAuditableEntity auditableEntity)
                {
                    auditableEntity.UpdatedAt = now;
                    if (actorId is not null) auditableEntity.UpdatedBy = actorId;
                }
            }
        }

        // 3. Audit trail (BR-18, UC-15): ai, làm gì, trên entity nào, thay đổi gì
        if (pending.Count > 0)
        {
            var workspaceBySeries = await ResolveSeriesWorkspacesAsync(pending.Select(p => p.Entry), cancellationToken);

            foreach (var (entry, action, changesJson) in pending)
            {
                var entityName = entry.Metadata.ClrType.Name;
                var entityId = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "Id")?.CurrentValue?.ToString();

                AuditLogs.Add(AuditLog.Create(
                    action: $"{action}_{entityName.ToUpperInvariant()}",
                    entityName: entityName,
                    entityId: entityId,
                    workspaceId: ResolveWorkspaceId(entry, workspaceBySeries),
                    userId: _currentUser?.UserId,
                    userEmail: _currentUser?.Email,
                    changesJson: changesJson,
                    ipAddress: _currentUser?.IpAddress,
                    details: $"{action} {entityName} (ID: {entityId})"
                ));
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>WorkspaceId của bản ghi audit: cột WorkspaceId, chính Workspace, hoặc suy ra qua SeriesId.</summary>
    private static Guid? ResolveWorkspaceId(EntityEntry entry, IReadOnlyDictionary<Guid, Guid> workspaceBySeries)
    {
        if (entry.Entity is StudioWorkspace workspace) return workspace.Id;
        if (entry.Metadata.FindProperty("WorkspaceId") is not null
            && entry.Property("WorkspaceId").CurrentValue is Guid wsId) return wsId;
        if (entry.Metadata.FindProperty("SeriesId") is not null
            && entry.Property("SeriesId").CurrentValue is Guid seriesId
            && workspaceBySeries.TryGetValue(seriesId, out var seriesWsId)) return seriesWsId;
        return null;
    }

    private async Task<IReadOnlyDictionary<Guid, Guid>> ResolveSeriesWorkspacesAsync(
        IEnumerable<EntityEntry> entries, CancellationToken cancellationToken)
    {
        var seriesIds = entries
            .Where(e => e.Metadata.FindProperty("WorkspaceId") is null && e.Metadata.FindProperty("SeriesId") is not null)
            .Select(e => e.Property("SeriesId").CurrentValue)
            .OfType<Guid>()
            .Distinct()
            .ToList();

        if (seriesIds.Count == 0) return new Dictionary<Guid, Guid>();

        // Series mới thêm trong cùng lần lưu chưa có trong DB → lấy từ ChangeTracker
        var result = ChangeTracker.Entries<Series>()
            .Where(e => seriesIds.Contains(e.Entity.Id))
            .ToDictionary(e => e.Entity.Id, e => e.Entity.WorkspaceId);

        var missing = seriesIds.Where(id => !result.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            var fromDb = await Series.IgnoreQueryFilters().AsNoTracking()
                .Where(s => missing.Contains(s.Id))
                .Select(s => new { s.Id, s.WorkspaceId })
                .ToListAsync(cancellationToken);
            foreach (var s in fromDb) result[s.Id] = s.WorkspaceId;
        }

        return result;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PanelForgeDbContext).Assembly);

        // 3. Tự động áp dụng Global Query Filter cho tất cả các Entity kế thừa ISoftDelete (chỉ khi IsDeleted thực sự được map vào bảng DB)
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ISoftDelete).IsAssignableFrom(entityType.ClrType))
            {
                var isDeletedProperty = entityType.FindProperty(nameof(ISoftDelete.IsDeleted));
                if (isDeletedProperty != null)
                {
                    var method = typeof(PanelForgeDbContext)
                        .GetMethod(nameof(ConfigureSoftDeleteFilter), BindingFlags.NonPublic | BindingFlags.Static)?
                        .MakeGenericMethod(entityType.ClrType);

                    method?.Invoke(null, [modelBuilder]);
                }
            }
        }
    }

    private static void ConfigureSoftDeleteFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ISoftDelete
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => !e.IsDeleted);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>()
                            .HaveColumnType("timestamptz");
    }
}
