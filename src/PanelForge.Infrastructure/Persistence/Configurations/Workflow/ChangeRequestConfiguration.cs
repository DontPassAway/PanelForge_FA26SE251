using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Workflow;

internal sealed class ChangeRequestConfiguration : IEntityTypeConfiguration<ChangeRequest>
{
    public void Configure(EntityTypeBuilder<ChangeRequest> builder)
    {
        builder.ToTable("change_requests");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(c => c.AssignmentId)
               .HasColumnName("assignment_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(c => c.AssignmentId)
               .HasDatabaseName("ix_change_requests_assignment_id");

        builder.Property(c => c.ChapterId)
               .HasColumnName("chapter_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(c => new { c.ChapterId, c.Status })
               .HasDatabaseName("ix_change_requests_chapter_status");

        builder.Property(c => c.AssetReferenceId)
               .HasColumnName("asset_reference_id")
               .HasColumnType("uuid");

        builder.Property(c => c.PanelId)
               .HasColumnName("panel_id")
               .HasColumnType("uuid");

        builder.Property(c => c.SubmissionId)
               .HasColumnName("submission_id")
               .HasColumnType("uuid");

        builder.Property(c => c.ResponsibleUserId)
               .HasColumnName("responsible_user_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(c => c.CreatedByUserId)
               .HasColumnName("created_by_user_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(c => c.Description)
               .HasColumnName("description")
               .HasColumnType("text")
               .IsRequired();

        builder.Property(c => c.RegionJson)
               .HasColumnName("region_json")
               .HasColumnType("text");

        builder.Property(c => c.DueDate)
               .HasColumnName("due_date")
               .HasColumnType("timestamptz");

        builder.Property(c => c.Status)
               .HasColumnName("status")
               .HasColumnType("varchar(30)")
               .HasConversion(
                   v => v.ToString(),
                   v => Enum.Parse<ChangeRequestStatus>(v))
               .IsRequired();

        builder.Property(c => c.ResolutionNote)
               .HasColumnName("resolution_note")
               .HasColumnType("text");

        builder.Property(c => c.ResolvedAt)
               .HasColumnName("resolved_at")
               .HasColumnType("timestamptz");

        builder.Property(c => c.ReviewerComment)
               .HasColumnName("reviewer_comment")
               .HasColumnType("text");

        builder.Property(c => c.ClosedByUserId)
               .HasColumnName("closed_by_user_id")
               .HasColumnType("uuid");

        builder.Property(c => c.ClosedAt)
               .HasColumnName("closed_at")
               .HasColumnType("timestamptz");

        builder.Property(c => c.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(c => c.UpdatedAt)
               .HasColumnName("updated_at")
               .HasColumnType("timestamptz");

        builder.HasOne<Assignment>()
               .WithMany()
               .HasForeignKey(c => c.AssignmentId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Submission>()
               .WithMany()
               .HasForeignKey(c => c.SubmissionId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}
