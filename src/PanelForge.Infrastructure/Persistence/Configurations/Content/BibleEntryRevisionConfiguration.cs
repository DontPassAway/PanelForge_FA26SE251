using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Bible;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class BibleEntryRevisionConfiguration : IEntityTypeConfiguration<BibleEntryRevision>
{
    public void Configure(EntityTypeBuilder<BibleEntryRevision> builder)
    {
        builder.ToTable("bible_entry_revisions");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(r => r.BibleEntryId)
               .HasColumnName("bible_entry_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(r => r.BibleEntryId)
               .HasDatabaseName("ix_bible_entry_revisions_bible_entry_id");

        builder.Property(r => r.VersionNumber)
               .HasColumnName("version_number")
               .HasColumnType("integer")
               .IsRequired();
        builder.HasIndex(r => new { r.BibleEntryId, r.VersionNumber })
               .IsUnique()
               .HasDatabaseName("ix_bible_entry_revisions_bible_entry_id_version");

        builder.Property(r => r.Summary)
               .HasColumnName("summary")
               .HasColumnType("varchar(500)")
               .IsRequired();

        builder.Property(r => r.SnapshotJson)
               .HasColumnName("snapshot_json")
               .HasColumnType("text")
               .IsRequired();

        builder.Property(r => r.AuthorId)
               .HasColumnName("author_id")
               .HasColumnType("uuid");

        builder.Property(r => r.ContentHash)
               .HasColumnName("content_hash")
               .HasColumnType("varchar(128)")
               .IsRequired();
        builder.HasIndex(r => r.ContentHash)
               .HasDatabaseName("ix_bible_entry_revisions_content_hash");

        builder.Property(r => r.AssociatedChapterId)
               .HasColumnName("associated_chapter_id")
               .HasColumnType("uuid");

        builder.Property(r => r.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();
    }
}
