using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class ScriptVersionConfiguration : IEntityTypeConfiguration<ScriptVersion>
{
    public void Configure(EntityTypeBuilder<ScriptVersion> builder)
    {
        builder.ToTable("script_versions");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();

        builder.Property(v => v.ChapterId).HasColumnName("chapter_id").HasColumnType("uuid").IsRequired();
        builder.HasOne<Chapter>().WithMany().HasForeignKey(v => v.ChapterId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(v => v.VersionNumber).HasColumnName("version_number").HasColumnType("integer").IsRequired();
        builder.HasIndex(v => new { v.ChapterId, v.VersionNumber })
               .IsUnique()
               .HasDatabaseName("ix_script_versions_chapter_version");

        builder.Property(v => v.Message)
               .HasColumnName("message")
               .HasColumnType($"varchar({ScriptVersion.MaxMessageLength})")
               .IsRequired();
        builder.Property(v => v.AuthorUserId).HasColumnName("author_user_id").HasColumnType("uuid").IsRequired();
        builder.Property(v => v.ContentHash).HasColumnName("content_hash").HasColumnType("varchar(64)").IsRequired();
        builder.Property(v => v.RowsJson).HasColumnName("rows_json").HasColumnType("jsonb").IsRequired();
        builder.Property(v => v.LineCount).HasColumnName("line_count").HasColumnType("integer").IsRequired();
        builder.Property(v => v.SubmittedAt).HasColumnName("submitted_at").HasColumnType("timestamptz");
        builder.Property(v => v.SubmittedById).HasColumnName("submitted_by_id").HasColumnType("uuid");
        builder.Property(v => v.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
    }
}
