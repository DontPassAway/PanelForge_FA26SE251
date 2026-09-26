using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

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

        // Chương đã xóa mềm không giữ chỗ số chương (Producer xóa rồi tạo lại chương N)
        builder.HasIndex(c => new { c.SeriesId, c.ChapterNumber })
               .IsUnique()
               .HasFilter("\"IsDeleted\" = false")
               .HasDatabaseName("ix_chapters_series_number");

        builder.Property(c => c.Status)
               .HasColumnName("status")
               .HasColumnType("varchar(20)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<ChapterStatus>(v))
               .HasDefaultValue(ChapterStatus.InProduction)
               .HasSentinel(ChapterStatus.InProduction)
               .IsRequired();

        builder.Property(c => c.PublishedAt)
               .HasColumnName("published_at")
               .HasColumnType("timestamptz");

        // Public catalog (BR-23/24) lọc theo trạng thái Published
        builder.HasIndex(c => new { c.Status, c.SeriesId })
               .HasDatabaseName("ix_chapters_status_series");

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
