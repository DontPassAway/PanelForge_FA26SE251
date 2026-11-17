using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Workflow;

internal sealed class StageTaskLotConfiguration : IEntityTypeConfiguration<StageTaskLot>
{
    public void Configure(EntityTypeBuilder<StageTaskLot> builder)
    {
        builder.ToTable("stage_task_lots");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();

        builder.Property(l => l.SeriesId).HasColumnName("series_id").HasColumnType("uuid").IsRequired();
        builder.Property(l => l.ChapterId).HasColumnName("chapter_id").HasColumnType("uuid").IsRequired();
        builder.Property(l => l.PipelineStageId).HasColumnName("pipeline_stage_id").HasColumnType("uuid").IsRequired();
        builder.Property(l => l.AssigneeUserId).HasColumnName("assignee_user_id").HasColumnType("uuid").IsRequired();
        builder.Property(l => l.AssignedByUserId).HasColumnName("assigned_by_user_id").HasColumnType("uuid").IsRequired();

        builder.Property(l => l.AssignedRole)
               .HasColumnName("assigned_role")
               .HasColumnType("varchar(30)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<WorkspaceRole>(v))
               .IsRequired();
        builder.Property(l => l.Status)
               .HasColumnName("status")
               .HasColumnType("varchar(30)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<StageTaskLotStatus>(v))
               .IsRequired();

        builder.Property(l => l.Brief).HasColumnName("brief").HasColumnType($"varchar({StageTaskLot.MaxBriefLength})");
        builder.Property(l => l.DueDate).HasColumnName("due_date").HasColumnType("timestamptz");
        builder.Property(l => l.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");

        builder.Property(l => l.RowVersion)
               .HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

        builder.HasIndex(l => new { l.ChapterId, l.PipelineStageId }).HasDatabaseName("ix_stage_task_lots_chapter_stage");
        builder.HasIndex(l => l.AssigneeUserId).HasDatabaseName("ix_stage_task_lots_assignee_user_id");

        builder.HasOne<Chapter>().WithMany().HasForeignKey(l => l.ChapterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PipelineStage>().WithMany().HasForeignKey(l => l.PipelineStageId).OnDelete(DeleteBehavior.Restrict);
    }
}
