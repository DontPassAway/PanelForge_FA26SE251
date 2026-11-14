using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class ChapterBibleBindingConfiguration : IEntityTypeConfiguration<ChapterBibleBinding>
{
    public void Configure(EntityTypeBuilder<ChapterBibleBinding> builder)
    {
        builder.ToTable("chapter_bible_bindings");

        builder.HasKey(b => new { b.ChapterId, b.BibleEntryId });

        builder.Property(b => b.ChapterId)
               .HasColumnName("chapter_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(b => b.BibleEntryId)
               .HasColumnName("bible_entry_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(b => b.BoundRevisionId)
               .HasColumnName("bound_revision_id")
               .HasColumnType("uuid");

        builder.Property(b => b.Source)
               .HasColumnName("source")
               .HasColumnType("varchar(16)")
               .HasConversion(
                   v => v.ToString(),
                   v => Enum.Parse<ChapterBibleBindingSource>(v))
               .IsRequired();

        builder.Property(b => b.ChosenByWriter)
               .HasColumnName("chosen_by_writer")
               .HasColumnType("boolean")
               .HasDefaultValue(false)
               .IsRequired();

        builder.Property(b => b.IsCrowdAtBind)
               .HasColumnName("is_crowd_at_bind")
               .HasColumnType("boolean")
               .HasDefaultValue(false)
               .IsRequired();

        builder.Property(b => b.BoundById)
               .HasColumnName("bound_by")
               .HasColumnType("uuid");

        builder.Property(b => b.BoundAt)
               .HasColumnName("bound_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.HasIndex(b => b.BibleEntryId)
               .HasDatabaseName("ix_chapter_bible_bindings_entry");

        builder.HasIndex(b => b.BoundRevisionId)
               .HasDatabaseName("ix_chapter_bible_bindings_revision");

        builder.HasOne(b => b.Chapter)
               .WithMany()
               .HasForeignKey(b => b.ChapterId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(b => b.BibleEntry)
               .WithMany()
               .HasForeignKey(b => b.BibleEntryId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.BoundRevision)
               .WithMany()
               .HasForeignKey(b => b.BoundRevisionId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.BoundBy)
               .WithMany()
               .HasForeignKey(b => b.BoundById)
               .OnDelete(DeleteBehavior.SetNull);
    }
}
