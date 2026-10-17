using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class ChapterSnapshotConfiguration : IEntityTypeConfiguration<ChapterSnapshot>
{
    public void Configure(EntityTypeBuilder<ChapterSnapshot> builder)
    {
        builder.ToTable("chapter_snapshots");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();

        builder.Property(s => s.ChapterId).HasColumnName("chapter_id").HasColumnType("uuid").IsRequired();
        builder.Property(s => s.SnapshotNumber).HasColumnName("snapshot_number").HasColumnType("int").IsRequired();
        builder.Property(s => s.ChapterVersion).HasColumnName("chapter_version").HasColumnType("bigint").IsRequired();
        builder.Property(s => s.CreatedByUserId).HasColumnName("created_by_user_id").HasColumnType("uuid").IsRequired();
        builder.Property(s => s.ManifestJson).HasColumnName("manifest_json").HasColumnType("text").IsRequired();
        builder.Property(s => s.ManifestHash).HasColumnName("manifest_hash").HasColumnType("varchar(64)").IsRequired();

        builder.Property(s => s.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("created_by").HasColumnType("varchar(100)");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by").HasColumnType("varchar(100)");

        // Append-only (BR-01): snapshot không bao giờ bị xoá, kể cả xoá mềm
        builder.Ignore(s => s.IsDeleted);
        builder.Ignore(s => s.DeletedAt);
        builder.Ignore(s => s.DeletedBy);

        builder.HasIndex(s => new { s.ChapterId, s.SnapshotNumber }).IsUnique()
               .HasDatabaseName("ux_chapter_snapshots_chapter_number");

        builder.HasOne<Chapter>().WithMany().HasForeignKey(s => s.ChapterId).OnDelete(DeleteBehavior.Restrict);
    }
}
