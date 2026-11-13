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
               .HasFilter("is_deleted = false")
               .HasDatabaseName("ix_chapters_series_number");

        builder.Property(c => c.Status)
               .HasColumnName("status")
               .HasColumnType("varchar(20)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<ChapterStatus>(v))
               .HasDefaultValue(ChapterStatus.PreProduction)
               .HasSentinel(ChapterStatus.PreProduction)
               .IsRequired();

        builder.Property(c => c.PublishedAt)
               .HasColumnName("published_at")
               .HasColumnType("timestamptz");

        builder.Property(c => c.IsLocked)
               .HasColumnName("is_locked")
               .HasColumnType("boolean")
               .HasDefaultValue(false)
               .IsRequired();

        builder.Property(c => c.PriceVnd)
               .HasColumnName("price_vnd")
               .HasColumnType("numeric(15,2)")
               .HasDefaultValue(0m)
               .IsRequired();

        // Public catalog (BR-23/24) lọc theo trạng thái Published
        builder.HasIndex(c => new { c.Status, c.SeriesId })
               .HasDatabaseName("ix_chapters_status_series");

        builder.Property(c => c.Title)
               .HasColumnName("title")
               .HasColumnType("varchar(500)");

        builder.Property(c => c.CoverImageSha256)
               .HasColumnName("cover_image_sha256")
               .HasColumnType("varchar(64)");

        builder.Property(c => c.Outline)
               .HasColumnName("outline")
               .HasColumnType("text");

        builder.Property(c => c.ScriptStatus)
               .HasColumnName("script_status")
               .HasColumnType("varchar(20)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<ScriptStatus>(v))
               .HasDefaultValue(ScriptStatus.Draft)
               .HasSentinel(ScriptStatus.Draft)
               .IsRequired();

        builder.Property(c => c.ScriptSubmittedAt)
               .HasColumnName("script_submitted_at")
               .HasColumnType("timestamptz");

        builder.Property(c => c.ScriptSubmittedById)
               .HasColumnName("script_submitted_by_id")
               .HasColumnType("uuid");

        builder.Property(c => c.IsScriptFrozen)
               .HasColumnName("is_script_frozen")
               .HasColumnType("boolean")
               .HasDefaultValue(false)
               .IsRequired();

        builder.Property(c => c.ScriptFrozenAt)
               .HasColumnName("script_frozen_at")
               .HasColumnType("timestamptz");

        builder.Property(c => c.ScriptFrozenById)
               .HasColumnName("script_frozen_by_id")
               .HasColumnType("uuid");

        builder.Property(c => c.ScriptContentHash)
               .HasColumnName("script_content_hash")
               .HasColumnType("varchar(64)");

        builder.Property(c => c.ScriptReopenStatus)
               .HasColumnName("script_reopen_status")
               .HasColumnType("varchar(20)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<ScriptReopenStatus>(v))
               .HasDefaultValue(ScriptReopenStatus.None)
               .HasSentinel(ScriptReopenStatus.None)
               .IsRequired();

        builder.Property(c => c.ScriptReopenReason)
               .HasColumnName("script_reopen_reason")
               .HasColumnType("text");

        builder.Property(c => c.ScriptReopenRequestedById)
               .HasColumnName("script_reopen_requested_by_id")
               .HasColumnType("uuid");

        builder.Property(c => c.ScriptReopenRequestedAt)
               .HasColumnName("script_reopen_requested_at")
               .HasColumnType("timestamptz");

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

        builder.HasOne(c => c.Script)
               .WithOne(s => s.Chapter)
               .HasForeignKey<ChapterScript>(s => s.ChapterId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Scenes)
               .WithOne(s => s.Chapter)
               .HasForeignKey(s => s.ChapterId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Pages)
               .WithOne(p => p.Chapter)
               .HasForeignKey(p => p.ChapterId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
