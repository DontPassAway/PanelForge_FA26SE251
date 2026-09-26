using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Workflow;

internal sealed class StageTransitionConfiguration : IEntityTypeConfiguration<StageTransition>
{
    public void Configure(EntityTypeBuilder<StageTransition> builder)
    {
        builder.ToTable("stage_transitions");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(t => t.PipelineDefinitionId)
               .HasColumnName("pipeline_definition_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(t => t.PipelineDefinitionId)
               .HasDatabaseName("ix_stage_transitions_pipeline_definition_id");

        builder.Property(t => t.FromStageId)
               .HasColumnName("from_stage_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(t => t.FromStageId)
               .HasDatabaseName("ix_stage_transitions_from_stage_id");

        builder.Property(t => t.ToStageId)
               .HasColumnName("to_stage_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(t => t.ToStageId)
               .HasDatabaseName("ix_stage_transitions_to_stage_id");

        // Transition soft-delete khi dựng lại pipeline không được chặn tạo lại cặp from→to
        builder.HasIndex(t => new { t.PipelineDefinitionId, t.FromStageId, t.ToStageId })
               .IsUnique()
               .HasFilter("\"IsDeleted\" = false")
               .HasDatabaseName("ix_stage_transitions_def_from_to");

        builder.Property(t => t.TransitionName)
               .HasColumnName("transition_name")
               .HasColumnType("varchar(150)")
               .IsRequired();

        builder.Property(t => t.RequiredRole)
               .HasColumnName("required_role")
               .HasColumnType("varchar(30)")
               .HasConversion(
                   v => v.HasValue ? v.Value.ToString() : null,
                   v => !string.IsNullOrEmpty(v) ? Enum.Parse<WorkspaceRole>(v) : null);

        builder.Property(t => t.RequiresComment)
               .HasColumnName("requires_comment")
               .HasColumnType("boolean")
               .HasDefaultValue(false)
               .IsRequired();

        builder.Property(t => t.IsBackwardTransition)
               .HasColumnName("is_backward_transition")
               .HasColumnType("boolean")
               .HasDefaultValue(false)
               .IsRequired();

        builder.HasOne(t => t.FromStage)
               .WithMany(s => s.OutgoingTransitions)
               .HasForeignKey(t => t.FromStageId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.ToStage)
               .WithMany(s => s.IncomingTransitions)
               .HasForeignKey(t => t.ToStageId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
