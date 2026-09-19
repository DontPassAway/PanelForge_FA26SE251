using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class BibleEntryConfiguration : IEntityTypeConfiguration<BibleEntry>
{
    public void Configure(EntityTypeBuilder<BibleEntry> builder)
    {
        builder.ToTable("bible_entries");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(e => e.SeriesBibleId)
               .HasColumnName("series_bible_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(e => e.SeriesBibleId)
               .HasDatabaseName("ix_bible_entries_series_bible_id");

        builder.Property(e => e.Category)
               .HasColumnName("category")
               .HasColumnType("varchar(30)")
               .HasConversion(
                   v => v.ToString(),
                   v => Enum.Parse<BibleEntryCategory>(v))
               .IsRequired();
        builder.HasIndex(e => e.Category)
               .HasDatabaseName("ix_bible_entries_category");

        builder.Property(e => e.Code)
               .HasColumnName("code")
               .HasColumnType("varchar(50)")
               .IsRequired();
        builder.HasIndex(e => new { e.SeriesBibleId, e.Code })
               .IsUnique()
               .HasDatabaseName("ix_bible_entries_series_bible_id_code");

        builder.Property(e => e.Name)
               .HasColumnName("name")
               .HasColumnType("varchar(255)")
               .IsRequired();

        builder.Property(e => e.Subtitle)
               .HasColumnName("subtitle")
               .HasColumnType("varchar(255)");

        builder.Property(e => e.Description)
               .HasColumnName("description")
               .HasColumnType("text")
               .IsRequired();

        builder.Property(e => e.DetailsJson)
               .HasColumnName("details_json")
               .HasColumnType("text")
               .IsRequired();

        builder.Property(e => e.ReferenceImageUrl)
               .HasColumnName("reference_image_url")
               .HasColumnType("text");

        builder.Property(e => e.Priority)
               .HasColumnName("priority")
               .HasColumnType("varchar(20)")
               .HasConversion(
                   v => v.ToString(),
                   v => Enum.Parse<BiblePriority>(v))
               .IsRequired();

        builder.Property(e => e.StrictCheck)
               .HasColumnName("strict_check")
               .HasColumnType("boolean")
               .HasDefaultValue(true)
               .IsRequired();

        builder.Property(e => e.IsActive)
               .HasColumnName("is_active")
               .HasColumnType("boolean")
               .HasDefaultValue(true)
               .IsRequired();

        builder.Property(e => e.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(e => e.UpdatedAt)
               .HasColumnName("updated_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.HasMany(e => e.Revisions)
               .WithOne(r => r.BibleEntry)
               .HasForeignKey(r => r.BibleEntryId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
