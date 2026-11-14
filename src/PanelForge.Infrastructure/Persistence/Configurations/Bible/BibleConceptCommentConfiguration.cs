using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Bible;

namespace PanelForge.Infrastructure.Persistence.Configurations.Bible;

internal sealed class BibleConceptCommentConfiguration : IEntityTypeConfiguration<BibleConceptComment>
{
    public void Configure(EntityTypeBuilder<BibleConceptComment> builder)
    {
        builder.ToTable("bible_concept_comments");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(c => c.RevisionId)
               .HasColumnName("revision_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(c => c.ConceptEventId)
               .HasColumnName("concept_event_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(c => c.ParentCommentId)
               .HasColumnName("parent_comment_id")
               .HasColumnType("uuid");

        builder.Property(c => c.AuthorId)
               .HasColumnName("author_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(c => c.Body)
               .HasColumnName("body")
               .HasColumnType("text")
               .IsRequired();

        builder.Property(c => c.Region)
               .HasColumnName("region")
               .HasColumnType("jsonb");

        builder.Property(c => c.ResolvedAt)
               .HasColumnName("resolved_at")
               .HasColumnType("timestamptz");

        builder.Property(c => c.ResolvedBy)
               .HasColumnName("resolved_by")
               .HasColumnType("uuid");

        builder.Property(c => c.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(c => c.CreatedBy)
               .HasColumnName("created_by")
               .HasColumnType("varchar(100)");

        builder.Property(c => c.UpdatedAt)
               .HasColumnName("updated_at")
               .HasColumnType("timestamptz");

        builder.Property(c => c.UpdatedBy)
               .HasColumnName("updated_by")
               .HasColumnType("varchar(100)");

        builder.Property(c => c.IsDeleted)
               .HasColumnName("is_deleted")
               .HasColumnType("boolean")
               .HasDefaultValue(false)
               .IsRequired();

        builder.Property(c => c.DeletedAt)
               .HasColumnName("deleted_at")
               .HasColumnType("timestamptz");

        builder.Property(c => c.DeletedBy)
               .HasColumnName("deleted_by")
               .HasColumnType("varchar(100)");

        builder.HasIndex(c => new { c.RevisionId, c.CreatedAt })
               .HasDatabaseName("ix_bible_concept_comments_revision");

        builder.HasOne(c => c.Revision)
               .WithMany()
               .HasForeignKey(c => c.RevisionId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.ConceptEvent)
               .WithMany()
               .HasForeignKey(c => c.ConceptEventId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.ParentComment)
               .WithMany(c => c.Replies)
               .HasForeignKey(c => c.ParentCommentId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
