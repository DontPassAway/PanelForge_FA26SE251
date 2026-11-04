using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Workflow;

internal sealed class AssignmentConfiguration : IEntityTypeConfiguration<Assignment>
{
    public void Configure(EntityTypeBuilder<Assignment> builder)
    {
        builder.ToTable("assignments");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(a => a.SeriesId)
               .HasColumnName("series_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(a => a.SeriesId)
               .HasDatabaseName("ix_assignments_series_id");

        builder.Property(a => a.ChapterId)
               .HasColumnName("chapter_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(a => a.ChapterId)
               .HasDatabaseName("ix_assignments_chapter_id");

        builder.Property(a => a.EntityType)
               .HasColumnName("entity_type")
               .HasColumnType("varchar(20)")
               .HasConversion(
                   v => v.ToString(),
                   v => Enum.Parse<WorkflowEntityType>(v))
               .IsRequired();

        builder.Property(a => a.TargetEntityId)
               .HasColumnName("target_entity_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(a => a.TargetEntityId)
               .HasDatabaseName("ix_assignments_target_entity_id");

        builder.Property(a => a.PipelineStageId)
               .HasColumnName("pipeline_stage_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(a => a.PipelineStageId)
               .HasDatabaseName("ix_assignments_pipeline_stage_id");

        builder.Property(a => a.AssigneeUserId)
               .HasColumnName("assignee_user_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(a => a.AssigneeUserId)
               .HasDatabaseName("ix_assignments_assignee_user_id");

        builder.Property(a => a.AssignedRole)
               .HasColumnName("assigned_role")
               .HasColumnType("varchar(30)")
               .HasConversion(
                   v => v.ToString(),
                   v => Enum.Parse<WorkspaceRole>(v))
               .IsRequired();

        builder.Property(a => a.Brief)
               .HasColumnName("brief")
               .HasColumnType("text")
               .IsRequired();

        builder.Property(a => a.ReferenceNotes)
               .HasColumnName("reference_notes")
               .HasColumnType("text");

        builder.Property(a => a.ReferenceAssetUrlsJson)
               .HasColumnName("reference_asset_urls_json")
               .HasColumnType("jsonb")
               .HasDefaultValueSql("'[]'::jsonb")
               .IsRequired();

        builder.Property(a => a.DueDate)
               .HasColumnName("due_date")
               .HasColumnType("timestamptz");
        builder.HasIndex(a => a.DueDate)
               .HasDatabaseName("ix_assignments_due_date");

        builder.Property(a => a.Priority)
               .HasColumnName("priority")
               .HasColumnType("varchar(20)")
               .HasConversion(
                   v => v.ToString(),
                   v => Enum.Parse<AssignmentPriority>(v))
               .IsRequired();

        builder.Property(a => a.Status)
               .HasColumnName("status")
               .HasColumnType("varchar(30)")
               .HasConversion(
                   v => v.ToString(),
                   v => Enum.Parse<AssignmentStatus>(v))
               .IsRequired();
        builder.HasIndex(a => a.Status)
               .HasDatabaseName("ix_assignments_status");

        builder.Property(a => a.DependsOnAssignmentId)
               .HasColumnName("depends_on_assignment_id")
               .HasColumnType("uuid");
        builder.HasIndex(a => a.DependsOnAssignmentId)
               .HasDatabaseName("ix_assignments_depends_on_assignment_id");
        builder.HasOne<Assignment>()
               .WithMany()
               .HasForeignKey(a => a.DependsOnAssignmentId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.Property(a => a.BatchId)
               .HasColumnName("batch_id")
               .HasColumnType("uuid");
        builder.HasIndex(a => a.BatchId)
               .HasDatabaseName("ix_assignments_batch_id");

        // Lớp chặn cuối ở DB: một đối tượng chỉ có một việc đang hiệu lực mỗi stage (việc huỷ = xoá mềm, không tính)
        builder.HasIndex(a => new { a.PipelineStageId, a.TargetEntityId, a.EntityType })
               .IsUnique()
               .HasFilter("is_deleted = false")
               .HasDatabaseName("uq_active_assignment");

        builder.Property(a => a.DependsOnAssignmentIdsJson)
               .HasColumnName("depends_on_assignment_ids_json")
               .HasColumnType("jsonb")
               .HasDefaultValueSql("'[]'::jsonb")
               .IsRequired();

        builder.Property(a => a.StartedAt)
               .HasColumnName("started_at")
               .HasColumnType("timestamptz");

        builder.Property(a => a.CompletedAt)
               .HasColumnName("completed_at")
               .HasColumnType("timestamptz");

        builder.Property(a => a.LastFeedbackComment)
               .HasColumnName("last_feedback_comment")
               .HasColumnType("text");

        builder.Property(a => a.ImpactReviewReason)
               .HasColumnName("impact_review_reason")
               .HasColumnType("text");

        builder.Property(a => a.ImpactReviewRequestedAt)
               .HasColumnName("impact_review_requested_at")
               .HasColumnType("timestamptz");

        builder.Property(a => a.HasScriptImpact)
               .HasColumnName("has_script_impact")
               .HasColumnType("boolean")
               .HasDefaultValue(false)
               .IsRequired();

        builder.Property(a => a.ImpactedScriptLineId)
               .HasColumnName("impacted_script_line_id")
               .HasColumnType("uuid");

        builder.Property(a => a.CancelReason)
               .HasColumnName("cancel_reason")
               .HasColumnType("text");

        builder.Property(a => a.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        // Chuyển giao / nộp / duyệt cùng lúc trên một việc: lần lưu sau thấy updated_at đã đổi → 409, không ghi đè quyền/phiên bản
        builder.Property(a => a.UpdatedAt)
               .HasColumnName("updated_at")
               .HasColumnType("timestamptz")
               .IsRequired()
               .IsConcurrencyToken();

        builder.HasOne(a => a.Series)
               .WithMany()
               .HasForeignKey(a => a.SeriesId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Chapter)
               .WithMany()
               .HasForeignKey(a => a.ChapterId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Stage)
               .WithMany(s => s.Assignments)
               .HasForeignKey(a => a.PipelineStageId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Assignee)
               .WithMany(u => u.AssignedTasks)
               .HasForeignKey(a => a.AssigneeUserId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
