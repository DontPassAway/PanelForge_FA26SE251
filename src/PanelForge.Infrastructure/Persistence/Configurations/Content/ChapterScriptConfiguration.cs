using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class ChapterScriptConfiguration : IEntityTypeConfiguration<ChapterScript>
{
    public void Configure(EntityTypeBuilder<ChapterScript> builder)
    {
        builder.ToTable("chapter_scripts");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(s => s.ChapterId)
               .HasColumnName("chapter_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.HasIndex(s => s.ChapterId)
               .IsUnique()
               .HasDatabaseName("ix_chapter_scripts_chapter_id");

        builder.Property(s => s.Version)
               .HasColumnName("version")
               .HasColumnType("integer")
               .HasDefaultValue(1)
               .IsRequired();

        builder.Property(s => s.RawText)
               .HasColumnName("raw_text")
               .HasColumnType("text")
               .IsRequired();

        builder.Property(s => s.IsLocked)
               .HasColumnName("is_locked")
               .HasColumnType("boolean")
               .HasDefaultValue(false)
               .IsRequired();

        builder.Property(s => s.LockedAt)
               .HasColumnName("locked_at")
               .HasColumnType("timestamptz");

        builder.Property(s => s.LockedById)
               .HasColumnName("locked_by_id")
               .HasColumnType("uuid");

        builder.Property(s => s.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(s => s.UpdatedAt)
               .HasColumnName("updated_at")
               .HasColumnType("timestamptz");
    }
}
