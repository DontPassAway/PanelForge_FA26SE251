using System.Reflection;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Auth;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;

namespace PanelForge.Infrastructure.Persistence;

public sealed class PanelForgeDbContext : DbContext, IPanelForgeDbContext
{
    public PanelForgeDbContext(DbContextOptions<PanelForgeDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<StudioWorkspace> StudioWorkspaces => Set<StudioWorkspace>();
    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();
    public DbSet<ExternalPreviewLink> ExternalPreviewLinks => Set<ExternalPreviewLink>();
    public DbSet<UserRememberedDevice> UserRememberedDevices => Set<UserRememberedDevice>();
    public DbSet<WorkspaceAiConfig> WorkspaceAiConfigs => Set<WorkspaceAiConfig>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<Series> Series => Set<Series>();
    public DbSet<SeriesPreset> SeriesPresets => Set<SeriesPreset>();
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

        foreach (var entry in ChangeTracker.Entries())
        {
            // 1. Tự động xử lý Audit Timestamps
            if (entry.Entity is IAuditableEntity auditable)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        auditable.CreatedAt = now;
                        break;
                    case EntityState.Modified:
                        auditable.UpdatedAt = now;
                        break;
                }
            }

            // 2. Tự động chuyển đổi xóa vật lý thành Soft-delete
            if (entry.Entity is ISoftDelete softDelete && entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                softDelete.IsDeleted = true;
                softDelete.DeletedAt = now;

                if (entry.Entity is IAuditableEntity auditableEntity)
                {
                    auditableEntity.UpdatedAt = now;
                }
            }
        }

        // 3. Tự động ghi vết kiểm toán hệ thống (Audit Trail - BR-18, UC-15)
        var auditEntries = new List<AuditLog>();
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is AuditLog || entry.State == EntityState.Detached || entry.State == EntityState.Unchanged)
                continue;

            var entityName = entry.Entity.GetType().Name;
            var action = entry.State switch
            {
                EntityState.Added => "CREATE",
                EntityState.Modified => "UPDATE",
                EntityState.Deleted => "DELETE",
                _ => entry.State.ToString()
            };

            var idProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "Id");
            var entityId = idProp?.CurrentValue?.ToString();

            Guid? wsId = null;
            var wsProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "WorkspaceId");
            if (wsProp?.CurrentValue is Guid g) wsId = g;

            var log = AuditLog.Create(
                action: $"{action}_{entityName.ToUpperInvariant()}",
                entityName: entityName,
                entityId: entityId,
                workspaceId: wsId,
                details: $"Audit Trail: {action} {entityName} (ID: {entityId})"
            );
            auditEntries.Add(log);
        }

        if (auditEntries.Count > 0)
        {
            AuditLogs.AddRange(auditEntries);
        }

        return await base.SaveChangesAsync(cancellationToken);
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
