using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Workflow;

internal sealed class WorkflowTransitionLogConfiguration : IEntityTypeConfiguration<WorkflowTransitionLog>
{
    public void Configure(EntityTypeBuilder<WorkflowTransitionLog> builder)
    {
        builder.ToTable("workflow_transition_logs");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(l => l.EntityId)
               .HasColumnName("entity_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(l => l.EntityId)
               .HasDatabaseName("ix_workflow_transition_logs_entity_id");

        builder.Property(l => l.EntityType)
               .HasColumnName("entity_type")
               .HasColumnType("varchar(20)")
               .HasConversion(
                   v => v.ToString(),
                   v => Enum.Parse<WorkflowEntityType>(v))
               .IsRequired();

        builder.Property(l => l.FromStageId)
               .HasColumnName("from_stage_id")
               .HasColumnType("uuid");

        builder.Property(l => l.ToStageId)
               .HasColumnName("to_stage_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(l => l.ToStageId)
               .HasDatabaseName("ix_workflow_transition_logs_to_stage_id");

        builder.Property(l => l.TriggeredByUserId)
               .HasColumnName("triggered_by_user_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(l => l.Comment)
               .HasColumnName("comment")
               .HasColumnType("text");

        builder.Property(l => l.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.HasOne(l => l.FromStage)
               .WithMany()
               .HasForeignKey(l => l.FromStageId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(l => l.ToStage)
               .WithMany()
               .HasForeignKey(l => l.ToStageId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
