using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Workflow;

internal sealed class PipelineStageConfiguration : IEntityTypeConfiguration<PipelineStage>
{
    public void Configure(EntityTypeBuilder<PipelineStage> builder)
    {
        builder.ToTable("pipeline_stages");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(s => s.PipelineDefinitionId)
               .HasColumnName("pipeline_definition_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(s => s.PipelineDefinitionId)
               .HasDatabaseName("ix_pipeline_stages_pipeline_definition_id");

        builder.Property(s => s.Name)
               .HasColumnName("name")
               .HasColumnType("varchar(100)")
               .IsRequired();

        builder.Property(s => s.Slug)
               .HasColumnName("slug")
               .HasColumnType("varchar(50)")
               .IsRequired();
        builder.HasIndex(s => new { s.PipelineDefinitionId, s.Slug })
               .IsUnique()
               .HasDatabaseName("ix_pipeline_stages_definition_slug");

        builder.Property(s => s.StageOrder)
               .HasColumnName("stage_order")
               .HasColumnType("integer")
               .IsRequired();
        builder.HasIndex(s => new { s.PipelineDefinitionId, s.StageOrder })
               .IsUnique()
               .HasDatabaseName("ix_pipeline_stages_definition_stage_order");

        builder.Property(s => s.ColorCode)
               .HasColumnName("color_code")
               .HasColumnType("varchar(20)");

        builder.Property(s => s.AllowedRole)
               .HasColumnName("allowed_role")
               .HasColumnType("varchar(30)")
               .HasConversion(
                   v => v.HasValue ? v.Value.ToString() : null,
                   v => !string.IsNullOrEmpty(v) ? Enum.Parse<WorkspaceRole>(v) : null);

        builder.Property(s => s.IsApprovalGate)
               .HasColumnName("is_approval_gate")
               .HasColumnType("boolean")
               .HasDefaultValue(false)
               .IsRequired();

        builder.Property(s => s.IsInitial)
               .HasColumnName("is_initial")
               .HasColumnType("boolean")
               .HasDefaultValue(false)
               .IsRequired();

        builder.Property(s => s.IsTerminal)
               .HasColumnName("is_terminal")
               .HasColumnType("boolean")
               .HasDefaultValue(false)
               .IsRequired();

        builder.Property(s => s.EstimatedDurationDays)
               .HasColumnName("estimated_duration_days")
               .HasColumnType("integer");
    }
}
