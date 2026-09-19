using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Workflow;

namespace PanelForge.Infrastructure.Persistence.Configurations.Workflow;

internal sealed class PipelineDefinitionConfiguration : IEntityTypeConfiguration<PipelineDefinition>
{
    public void Configure(EntityTypeBuilder<PipelineDefinition> builder)
    {
        builder.ToTable("pipeline_definitions");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(p => p.WorkspaceId)
               .HasColumnName("workspace_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(p => p.WorkspaceId)
               .HasDatabaseName("ix_pipeline_definitions_workspace_id");

        builder.Property(p => p.SeriesId)
               .HasColumnName("series_id")
               .HasColumnType("uuid");
        builder.HasIndex(p => p.SeriesId)
               .HasDatabaseName("ix_pipeline_definitions_series_id");

        builder.Property(p => p.Name)
               .HasColumnName("name")
               .HasColumnType("varchar(255)")
               .IsRequired();

        builder.Property(p => p.Description)
               .HasColumnName("description")
               .HasColumnType("text");

        builder.Property(p => p.IsDefault)
               .HasColumnName("is_default")
               .HasColumnType("boolean")
               .HasDefaultValue(false)
               .IsRequired();

        builder.Property(p => p.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(p => p.UpdatedAt)
               .HasColumnName("updated_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.HasOne(p => p.Workspace)
               .WithMany()
               .HasForeignKey(p => p.WorkspaceId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Series)
               .WithOne(s => s.PipelineDefinition)
               .HasForeignKey<PipelineDefinition>(p => p.SeriesId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(p => p.Stages)
               .WithOne(s => s.PipelineDefinition)
               .HasForeignKey(s => s.PipelineDefinitionId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Transitions)
               .WithOne(t => t.PipelineDefinition)
               .HasForeignKey(t => t.PipelineDefinitionId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
