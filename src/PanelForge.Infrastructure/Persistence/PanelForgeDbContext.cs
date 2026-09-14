using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Auth;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Infrastructure.Persistence;

public sealed class PanelForgeDbContext : DbContext, IPanelForgeDbContext
{
    public PanelForgeDbContext(DbContextOptions<PanelForgeDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<StudioWorkspace> StudioWorkspaces => Set<StudioWorkspace>();
    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();
    public DbSet<ExternalPreviewLink> ExternalPreviewLinks => Set<ExternalPreviewLink>();

    public DbSet<Series> Series => Set<Series>();
    public DbSet<Chapter> Chapters => Set<Chapter>();
    public DbSet<Scene> Scenes => Set<Scene>();
    public DbSet<ScriptLine> ScriptLines => Set<ScriptLine>();
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<Panel> Panels => Set<Panel>();
    public DbSet<Element> Elements => Set<Element>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PanelForgeDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>()
                            .HaveColumnType("timestamptz");
    }
}
