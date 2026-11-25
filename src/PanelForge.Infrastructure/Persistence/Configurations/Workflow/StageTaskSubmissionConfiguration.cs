using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Workflow;

internal sealed class StageTaskSubmissionConfiguration : IEntityTypeConfiguration<StageTaskSubmission>
{
    public void Configure(EntityTypeBuilder<StageTaskSubmission> builder)
    {
        builder.ToTable("stage_task_submissions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();

        builder.Property(s => s.StageTaskId).HasColumnName("stage_task_id").HasColumnType("uuid").IsRequired();
        builder.Property(s => s.ChapterId).HasColumnName("chapter_id").HasColumnType("uuid").IsRequired();
        builder.Property(s => s.SubmittedByUserId).HasColumnName("submitted_by_user_id").HasColumnType("uuid").IsRequired();
        builder.Property(s => s.AttemptNumber).HasColumnName("attempt_number").IsRequired();
        builder.Property(s => s.Status)
               .HasColumnName("status")
               .HasColumnType("varchar(30)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<SubmissionStatus>(v))
               .IsRequired();
        builder.Property(s => s.SubmittedAt).HasColumnName("submitted_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(s => s.ArtworkReferenceIds).HasColumnName("artwork_reference_ids").HasColumnType("uuid[]").IsRequired();
        builder.Property(s => s.PanelLayoutJson).HasColumnName("panel_layout_json").HasColumnType("jsonb")
               .HasDefaultValueSql("'[]'::jsonb").IsRequired();
        builder.Property(s => s.CanvasVersionsJson).HasColumnName("canvas_versions_json").HasColumnType("jsonb")
               .HasDefaultValueSql("'{}'::jsonb").IsRequired();
        builder.Property(s => s.ScriptVersionId).HasColumnName("script_version_id").HasColumnType("uuid");
        builder.Property(s => s.DecidedByUserId).HasColumnName("decided_by_user_id").HasColumnType("uuid");
        builder.Property(s => s.DecidedAt).HasColumnName("decided_at").HasColumnType("timestamptz");
        builder.Property(s => s.DecisionComment).HasColumnName("decision_comment").HasColumnType("text");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");

        builder.HasIndex(s => new { s.StageTaskId, s.AttemptNumber }).IsUnique()
               .HasDatabaseName("ux_stage_task_submissions_task_attempt");
        builder.HasIndex(s => s.ChapterId).HasDatabaseName("ix_stage_task_submissions_chapter_id");

        builder.HasOne<StageTask>().WithMany().HasForeignKey(s => s.StageTaskId).OnDelete(DeleteBehavior.Restrict);
    }
}
