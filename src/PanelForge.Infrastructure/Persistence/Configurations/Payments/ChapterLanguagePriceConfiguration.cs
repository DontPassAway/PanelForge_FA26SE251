using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Payments;

namespace PanelForge.Infrastructure.Persistence.Configurations.Payments;

internal sealed class ChapterLanguagePriceConfiguration : IEntityTypeConfiguration<ChapterLanguagePrice>
{
    public void Configure(EntityTypeBuilder<ChapterLanguagePrice> builder)
    {
        builder.ToTable("chapter_language_prices");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(p => p.ChapterId).HasColumnName("chapter_id").HasColumnType("uuid").IsRequired();
        builder.Property(p => p.Language).HasColumnName("language").HasColumnType("varchar(8)").IsRequired();
        builder.Property(p => p.PriceVnd).HasColumnName("price_vnd").HasColumnType("numeric(15,2)").IsRequired();
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
        builder.Property(p => p.IsDeleted).HasColumnName("is_deleted").HasColumnType("boolean").HasDefaultValue(false).IsRequired();

        builder.HasIndex(p => new { p.ChapterId, p.Language })
               .IsUnique()
               .HasFilter("is_deleted = false")
               .HasDatabaseName("ix_chapter_language_prices_chapter_language");

        builder.HasOne(p => p.Chapter).WithMany().HasForeignKey(p => p.ChapterId).OnDelete(DeleteBehavior.Cascade);
    }
}
