using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class ReaderLibraryItemConfiguration : IEntityTypeConfiguration<ReaderLibraryItem>
{
    public void Configure(EntityTypeBuilder<ReaderLibraryItem> builder)
    {
        builder.ToTable("reader_library_items");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(i => i.UserId).HasColumnName("user_id").HasColumnType("uuid").IsRequired();
        builder.Property(i => i.SeriesId).HasColumnName("series_id").HasColumnType("uuid").IsRequired();
        builder.Property(i => i.List)
               .HasColumnName("list")
               .HasColumnType("varchar(20)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<ReaderLibraryList>(v))
               .IsRequired();
        builder.Property(i => i.LastSeenAt).HasColumnName("last_seen_at").HasColumnType("timestamptz");
        builder.Property(i => i.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
        builder.Property(i => i.IsDeleted).HasColumnName("is_deleted").HasColumnType("boolean").HasDefaultValue(false).IsRequired();

        // Một bộ truyện xuất hiện tối đa một lần trong mỗi danh sách của một độc giả.
        builder.HasIndex(i => new { i.UserId, i.List, i.SeriesId })
               .IsUnique()
               .HasFilter("is_deleted = false")
               .HasDatabaseName("ix_reader_library_items_user_list_series");

        builder.HasOne<Series>().WithMany().HasForeignKey(i => i.SeriesId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ReaderReadingProgressConfiguration : IEntityTypeConfiguration<ReaderReadingProgress>
{
    public void Configure(EntityTypeBuilder<ReaderReadingProgress> builder)
    {
        builder.ToTable("reader_reading_progress");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(p => p.UserId).HasColumnName("user_id").HasColumnType("uuid").IsRequired();
        builder.Property(p => p.SeriesId).HasColumnName("series_id").HasColumnType("uuid").IsRequired();
        builder.Property(p => p.ChapterId).HasColumnName("chapter_id").HasColumnType("uuid").IsRequired();
        builder.Property(p => p.ChapterNumber).HasColumnName("chapter_number").HasColumnType("numeric(8,2)").IsRequired();
        builder.Property(p => p.LastPageNumber).HasColumnName("last_page_number").HasColumnType("int").IsRequired();
        builder.Property(p => p.TotalPages).HasColumnName("total_pages").HasColumnType("int").IsRequired();
        builder.Property(p => p.Language).HasColumnName("language").HasColumnType("varchar(8)").HasDefaultValue("vi").IsRequired();
        builder.Property(p => p.ReadAt).HasColumnName("read_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
        builder.Property(p => p.IsDeleted).HasColumnName("is_deleted").HasColumnType("boolean").HasDefaultValue(false).IsRequired();

        builder.HasIndex(p => new { p.UserId, p.SeriesId })
               .IsUnique()
               .HasFilter("is_deleted = false")
               .HasDatabaseName("ix_reader_reading_progress_user_series");
        builder.HasIndex(p => new { p.UserId, p.ReadAt }).HasDatabaseName("ix_reader_reading_progress_user_read_at");

        builder.HasOne<Series>().WithMany().HasForeignKey(p => p.SeriesId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Chapter>().WithMany().HasForeignKey(p => p.ChapterId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
