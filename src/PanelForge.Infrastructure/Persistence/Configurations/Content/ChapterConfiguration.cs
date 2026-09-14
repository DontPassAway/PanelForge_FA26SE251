using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class ChapterConfiguration : IEntityTypeConfiguration<Chapter>
{
    public void Configure(EntityTypeBuilder<Chapter> builder)
    {
        builder.ToTable("chapters");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(c => c.SeriesId)
               .HasColumnName("series_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(c => c.SeriesId)
               .HasDatabaseName("ix_chapters_series_id");

        builder.Property(c => c.ChapterNumber)
               .HasColumnName("chapter_number")
               .HasColumnType("numeric(8,2)")
               .IsRequired();

        builder.HasIndex(c => new { c.SeriesId, c.ChapterNumber })
               .IsUnique()
               .HasDatabaseName("ix_chapters_series_number");

        builder.Property(c => c.Title)
               .HasColumnName("title")
               .HasColumnType("varchar(500)");

        builder.Property(c => c.TargetReleaseDate)
               .HasColumnName("target_release_date")
               .HasColumnType("date");

        builder.Property(c => c.VersionVector)
               .HasColumnName("version_vector")
               .HasColumnType("bigint")
               .HasDefaultValue(0L)
               .IsConcurrencyToken()
               .IsRequired();

        builder.Property(c => c.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.HasMany(c => c.Scenes)
               .WithOne(s => s.Chapter)
               .HasForeignKey(s => s.ChapterId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Pages)
               .WithOne(p => p.Chapter)
               .HasForeignKey(p => p.ChapterId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.ExternalPreviewLinks)
               .WithOne()
               .HasForeignKey(l => l.ChapterId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
