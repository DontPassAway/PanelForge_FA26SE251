using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Workflow;

internal sealed class SubmissionConfiguration : IEntityTypeConfiguration<Submission>
{
    public void Configure(EntityTypeBuilder<Submission> builder)
    {
        builder.ToTable("submissions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(s => s.AssignmentId)
               .HasColumnName("assignment_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(s => new { s.AssignmentId, s.AttemptNumber })
               .IsUnique()
               .HasDatabaseName("ix_submissions_assignment_attempt");

        builder.Property(s => s.ChapterId)
               .HasColumnName("chapter_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(s => s.ChapterId)
               .HasDatabaseName("ix_submissions_chapter_id");

        builder.Property(s => s.SubmittedByUserId)
               .HasColumnName("submitted_by_user_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(s => s.AttemptNumber)
               .HasColumnName("attempt_number")
               .HasColumnType("int")
               .IsRequired();

        builder.Property(s => s.Status)
               .HasColumnName("status")
               .HasColumnType("varchar(30)")
               .HasConversion(
                   v => v.ToString(),
                   v => Enum.Parse<SubmissionStatus>(v))
               .IsRequired();

        builder.Property(s => s.SubmittedAt)
               .HasColumnName("submitted_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(s => s.DecidedByUserId)
               .HasColumnName("decided_by_user_id")
               .HasColumnType("uuid");

        builder.Property(s => s.DecidedAt)
               .HasColumnName("decided_at")
               .HasColumnType("timestamptz");

        builder.Property(s => s.DecisionComment)
               .HasColumnName("decision_comment")
               .HasColumnType("text");

        // Bản được nộp: các AssetReference (uuid[]) và version chương lúc nộp
        builder.Property(s => s.ArtworkReferenceIds)
               .HasColumnName("artwork_reference_ids")
               .HasColumnType("uuid[]")
               .IsRequired();

        builder.Property(s => s.ChapterVersion)
               .HasColumnName("chapter_version")
               .HasColumnType("bigint")
               .IsRequired();

        builder.Property(s => s.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(s => s.UpdatedAt)
               .HasColumnName("updated_at")
               .HasColumnType("timestamptz");

        builder.HasOne<Assignment>()
               .WithMany()
               .HasForeignKey(s => s.AssignmentId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
