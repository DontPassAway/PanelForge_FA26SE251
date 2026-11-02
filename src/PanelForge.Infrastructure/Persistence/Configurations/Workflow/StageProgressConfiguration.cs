using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Workflow;

internal sealed class StageProgressConfiguration : IEntityTypeConfiguration<StageProgress>
{
    public void Configure(EntityTypeBuilder<StageProgress> builder)
    {
        builder.ToTable("stage_progress");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(s => s.PanelId)
               .HasColumnName("panel_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(s => s.PipelineStageId)
               .HasColumnName("pipeline_stage_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.HasIndex(s => new { s.PanelId, s.PipelineStageId })
               .IsUnique()
               .HasDatabaseName("ix_stage_progress_panel_stage");
        builder.HasIndex(s => s.PipelineStageId)
               .HasDatabaseName("ix_stage_progress_pipeline_stage_id");

        builder.Property(s => s.Status)
               .HasColumnName("status")
               .HasColumnType("varchar(30)")
               .HasConversion(
                   v => v.ToString(),
                   v => Enum.Parse<StageProgressStatus>(v))
               .IsRequired();

        builder.Property(s => s.SubmittedAt)
               .HasColumnName("submitted_at")
               .HasColumnType("timestamptz");

        builder.Property(s => s.SubmittedByUserId)
               .HasColumnName("submitted_by_user_id")
               .HasColumnType("uuid");

        builder.Property(s => s.HasScriptImpact)
               .HasColumnName("has_script_impact")
               .HasColumnType("boolean")
               .HasDefaultValue(false)
               .IsRequired();

        builder.Property(s => s.ImpactedScriptLineId)
               .HasColumnName("impacted_script_line_id")
               .HasColumnType("uuid");

        builder.Property(s => s.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(s => s.UpdatedAt)
               .HasColumnName("updated_at")
               .HasColumnType("timestamptz");

        builder.HasOne<Panel>()
               .WithMany()
               .HasForeignKey(s => s.PanelId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<PipelineStage>()
               .WithMany()
               .HasForeignKey(s => s.PipelineStageId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
