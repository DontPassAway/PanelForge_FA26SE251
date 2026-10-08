using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class ScriptLineBibleLinkConfiguration : IEntityTypeConfiguration<ScriptLineBibleLink>
{
    public void Configure(EntityTypeBuilder<ScriptLineBibleLink> builder)
    {
        builder.ToTable("script_line_bible_links");

        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(k => k.ScriptLineId).HasColumnName("script_line_id").HasColumnType("uuid").IsRequired();
        builder.Property(k => k.BibleEntryId).HasColumnName("bible_entry_id").HasColumnType("uuid").IsRequired();
        builder.Property(k => k.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(k => k.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");

        // Một liên kết đang hoạt động cho mỗi cặp; liên kết đã gỡ (xoá mềm) giữ lại làm lịch sử
        builder.HasIndex(k => new { k.ScriptLineId, k.BibleEntryId })
               .IsUnique()
               .HasFilter("is_deleted = false")
               .HasDatabaseName("ux_script_line_bible_links_line_entry");
        // Truy ngược "những dòng nào nhắc tới mục Bible này" (kiểm tra nhất quán tăng dần)
        builder.HasIndex(k => k.BibleEntryId).HasDatabaseName("ix_script_line_bible_links_bible_entry_id");

        builder.HasOne<ScriptLine>().WithMany().HasForeignKey(k => k.ScriptLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BibleEntry>().WithMany().HasForeignKey(k => k.BibleEntryId).OnDelete(DeleteBehavior.Restrict);
    }
}
