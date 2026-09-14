using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class PageConfiguration : IEntityTypeConfiguration<Page>
{
    public void Configure(EntityTypeBuilder<Page> builder)
    {
        builder.ToTable("pages");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(p => p.ChapterId)
               .HasColumnName("chapter_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(p => p.SceneId)
               .HasColumnName("scene_id")
               .HasColumnType("uuid");

        builder.Property(p => p.PageNumber)
               .HasColumnName("page_number")
               .HasColumnType("int")
               .IsRequired();

        builder.HasIndex(p => new { p.ChapterId, p.PageNumber })
               .IsUnique()
               .HasDatabaseName("ix_pages_chapter_number");

        builder.Property(p => p.LayoutFormat)
               .HasColumnName("layout_format")
               .HasColumnType("varchar(30)")
               .HasConversion(
                   v => v.ToString(),
                   v => Enum.Parse<LayoutFormat>(v))
               .IsRequired();

        builder.Property(p => p.WidthPx)
               .HasColumnName("width_px")
               .HasColumnType("int")
               .IsRequired();

        builder.Property(p => p.HeightPx)
               .HasColumnName("height_px")
               .HasColumnType("int")
               .IsRequired();

        builder.Property(p => p.Dpi)
               .HasColumnName("dpi")
               .HasColumnType("int")
               .HasDefaultValue(300)
               .IsRequired();

        builder.HasMany(p => p.Panels)
               .WithOne(pa => pa.Page)
               .HasForeignKey(pa => pa.PageId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
