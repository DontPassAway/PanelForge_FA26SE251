using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class ChapterEditionConfiguration : IEntityTypeConfiguration<ChapterEdition>
{
    public void Configure(EntityTypeBuilder<ChapterEdition> builder)
    {
        builder.ToTable("chapter_editions");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(e => e.ChapterId)
               .HasColumnName("chapter_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(e => e.LanguageCode)
               .HasColumnName("language_code")
               .HasColumnType("varchar(10)")
               .IsRequired();

        builder.HasIndex(e => new { e.ChapterId, e.LanguageCode })
               .IsUnique()
               .HasDatabaseName("uq_chapter_edition");

        builder.Property(e => e.Status)
               .HasColumnName("status")
               .HasColumnType("varchar(20)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<EditionStatus>(v))
               .HasDefaultValue(EditionStatus.Draft)
               .IsRequired();

        builder.Property(e => e.IsPaid)
               .HasColumnName("is_paid")
               .HasColumnType("boolean")
               .HasDefaultValue(false)
               .IsRequired();

        builder.Property(e => e.PriceVnd)
               .HasColumnName("price_vnd")
               .HasColumnType("numeric(12,2)")
               .HasDefaultValue(0m)
               .IsRequired();

        builder.Property(e => e.FreePreviewPages)
               .HasColumnName("free_preview_pages")
               .HasColumnType("int")
               .HasDefaultValue(0)
               .IsRequired();

        builder.Property(e => e.CurrentSnapshotId)
               .HasColumnName("current_snapshot_id")
               .HasColumnType("uuid");

        builder.Property(e => e.TranslatorUserId)
               .HasColumnName("translator_user_id")
               .HasColumnType("uuid");

        builder.Property(e => e.ApprovedAt)
               .HasColumnName("approved_at")
               .HasColumnType("timestamptz");

        builder.Property(e => e.ApprovedById)
               .HasColumnName("approved_by")
               .HasColumnType("uuid");

        builder.Property(e => e.PublishedAt)
               .HasColumnName("published_at")
               .HasColumnType("timestamptz");

        builder.Property(e => e.Version)
               .HasColumnName("version")
               .HasColumnType("bigint")
               .HasDefaultValue(0L)
               .IsRequired();

        builder.Property(e => e.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(e => e.UpdatedAt)
               .HasColumnName("updated_at")
               .HasColumnType("timestamptz");

        builder.Property(e => e.CreatedBy)
               .HasColumnName("created_by")
               .HasColumnType("text");

        builder.Property(e => e.UpdatedBy)
               .HasColumnName("updated_by")
               .HasColumnType("text");

        builder.Property(e => e.IsDeleted)
               .HasColumnName("is_deleted")
               .HasColumnType("boolean")
               .HasDefaultValue(false)
               .IsRequired();

        builder.Property(e => e.DeletedAt)
               .HasColumnName("deleted_at")
               .HasColumnType("timestamptz");

        builder.Property(e => e.DeletedBy)
               .HasColumnName("deleted_by")
               .HasColumnType("text");

        builder.HasOne(e => e.Chapter)
               .WithMany(c => c.Editions)
               .HasForeignKey(e => e.ChapterId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.CurrentSnapshot)
               .WithMany()
               .HasForeignKey(e => e.CurrentSnapshotId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.TranslatorUser)
               .WithMany()
               .HasForeignKey(e => e.TranslatorUserId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}
