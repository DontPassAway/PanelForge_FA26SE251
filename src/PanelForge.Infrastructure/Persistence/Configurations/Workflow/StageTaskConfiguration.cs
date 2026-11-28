using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Workflow;

internal sealed class StageTaskConfiguration : IEntityTypeConfiguration<StageTask>
{
    public void Configure(EntityTypeBuilder<StageTask> builder)
    {
        builder.ToTable("stage_tasks");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();

        builder.Property(t => t.SeriesId).HasColumnName("series_id").HasColumnType("uuid").IsRequired();
        builder.Property(t => t.ChapterId).HasColumnName("chapter_id").HasColumnType("uuid").IsRequired();
        builder.Property(t => t.PipelineStageId).HasColumnName("pipeline_stage_id").HasColumnType("uuid").IsRequired();
        builder.Property(t => t.PageId).HasColumnName("page_id").HasColumnType("uuid");
        builder.Property(t => t.LotId).HasColumnName("lot_id").HasColumnType("uuid");
        builder.Property(t => t.ReviewerUserId).HasColumnName("reviewer_user_id").HasColumnType("uuid");
        builder.Property(t => t.CurrentSubmissionId).HasColumnName("current_submission_id").HasColumnType("uuid");

        builder.Property(t => t.Status)
               .HasColumnName("status")
               .HasColumnType("varchar(30)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<StageTaskStatus>(v))
               .IsRequired();

        builder.Property(t => t.IsInputOutdated)
               .HasColumnName("is_input_outdated").HasColumnType("boolean").HasDefaultValue(false).IsRequired();
        builder.Property(t => t.OutdatedReason)
               .HasColumnName("outdated_reason").HasColumnType($"varchar({StageTask.MaxOutdatedReasonLength})");
        builder.Property(t => t.ReopenReason)
               .HasColumnName("reopen_reason").HasColumnType($"varchar({StageTask.MaxOutdatedReasonLength})");
        builder.Property(t => t.FlaggedPanelIds)
               .HasColumnName("flagged_panel_ids").HasColumnType("uuid[]").HasDefaultValueSql("'{}'::uuid[]").IsRequired();
        builder.Property(t => t.StartedAt).HasColumnName("started_at").HasColumnType("timestamptz");
        builder.Property(t => t.ReadyAt).HasColumnName("ready_at").HasColumnType("timestamptz");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");

        // xmin thay cho UpdatedAt làm concurrency token: tránh 409 giả do lệch độ chính xác timestamptz khi DTO đi qua JSON
        builder.Property(t => t.RowVersion)
               .HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

        // Mỗi (Chương, Công đoạn) có một ô cấp Chương, mỗi (Chương, Công đoạn, Trang) một ô cấp Trang.
        // Hai partial index thay cho NULLS NOT DISTINCT; bỏ qua bản ghi xoá mềm.
        builder.HasIndex(t => new { t.ChapterId, t.PipelineStageId })
               .IsUnique()
               .HasFilter("page_id IS NULL AND is_deleted = false")
               .HasDatabaseName("ux_stage_tasks_chapter_stage");
        builder.HasIndex(t => new { t.ChapterId, t.PipelineStageId, t.PageId })
               .IsUnique()
               .HasFilter("page_id IS NOT NULL AND is_deleted = false")
               .HasDatabaseName("ux_stage_tasks_chapter_stage_page");
        builder.HasIndex(t => t.LotId).HasDatabaseName("ix_stage_tasks_lot_id");
        builder.HasIndex(t => t.PageId).HasDatabaseName("ix_stage_tasks_page_id");

        builder.HasOne<Chapter>().WithMany().HasForeignKey(t => t.ChapterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Page>().WithMany().HasForeignKey(t => t.PageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PipelineStage>().WithMany().HasForeignKey(t => t.PipelineStageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StageTaskLot>().WithMany().HasForeignKey(t => t.LotId).OnDelete(DeleteBehavior.SetNull);
    }
}
