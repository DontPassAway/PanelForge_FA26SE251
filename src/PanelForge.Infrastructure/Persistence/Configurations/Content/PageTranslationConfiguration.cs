using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class PageTranslationConfiguration : IEntityTypeConfiguration<PageTranslation>
{
    public void Configure(EntityTypeBuilder<PageTranslation> builder)
    {
        builder.ToTable("page_translations");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(t => t.PageId).HasColumnName("page_id").HasColumnType("uuid").IsRequired();
        builder.Property(t => t.ChapterId).HasColumnName("chapter_id").HasColumnType("uuid").IsRequired();
        builder.Property(t => t.Language).HasColumnName("language").HasColumnType("varchar(8)").IsRequired();
        builder.Property(t => t.ContentSha256).HasColumnName("content_sha256").HasColumnType("varchar(64)").IsRequired();
        builder.Property(t => t.ContentType).HasColumnName("content_type").HasColumnType("varchar(50)").IsRequired();
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
        builder.Property(t => t.IsDeleted).HasColumnName("is_deleted").HasColumnType("boolean").HasDefaultValue(false).IsRequired();

        builder.HasIndex(t => new { t.PageId, t.Language })
               .IsUnique()
               .HasFilter("is_deleted = false")
               .HasDatabaseName("ix_page_translations_page_language");
        builder.HasIndex(t => new { t.ChapterId, t.Language }).HasDatabaseName("ix_page_translations_chapter_language");

        builder.HasOne(t => t.Page)
               .WithMany()
               .HasForeignKey(t => t.PageId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
