using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class ChapterViewConfiguration : IEntityTypeConfiguration<ChapterView>
{
    public void Configure(EntityTypeBuilder<ChapterView> builder)
    {
        builder.ToTable("chapter_views");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(v => v.ChapterId).HasColumnName("chapter_id").HasColumnType("uuid").IsRequired();
        builder.Property(v => v.SeriesId).HasColumnName("series_id").HasColumnType("uuid").IsRequired();
        builder.Property(v => v.ViewerKey).HasColumnName("viewer_key")
               .HasColumnType($"varchar({ChapterView.MaxViewerKeyLength})").IsRequired();
        builder.Property(v => v.ViewedAt).HasColumnName("viewed_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(v => v.Language)
               .HasColumnName("language")
               .HasColumnType("varchar(8)")
               .HasDefaultValue("vi")
               .IsRequired();


        // Đếm theo bộ truyện + khoảng thời gian (Top Ngày/Tuần/Tháng) và kiểm tra trùng lượt của một người xem.
        builder.HasIndex(v => new { v.SeriesId, v.ViewedAt }).HasDatabaseName("ix_chapter_views_series_viewed_at");
        builder.HasIndex(v => new { v.ChapterId, v.ViewerKey, v.ViewedAt }).HasDatabaseName("ix_chapter_views_chapter_viewer");
    }
}
