using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Identity;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class ChapterScriptWorkingCopyConfiguration : IEntityTypeConfiguration<ChapterScriptWorkingCopy>
{
    public void Configure(EntityTypeBuilder<ChapterScriptWorkingCopy> builder)
    {
        builder.ToTable("chapter_script_working_copies");

        builder.HasKey(w => w.ChapterId);
        builder.Property(w => w.ChapterId).HasColumnName("chapter_id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(w => w.WriterUserId).HasColumnName("writer_user_id").HasColumnType("uuid").IsRequired();
        builder.Property(w => w.BaseVersionId).HasColumnName("base_version_id").HasColumnType("uuid").IsRequired();
        builder.Property(w => w.ContentJson).HasColumnName("content_json").HasColumnType("jsonb").IsRequired();
        builder.Property(w => w.ContentHash).HasColumnName("content_hash").HasColumnType("varchar(64)").IsRequired();
        builder.Property(w => w.LineCount).HasColumnName("line_count").HasColumnType("integer").HasDefaultValue(0).IsRequired();
        builder.Property(w => w.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasOne<Chapter>()
               .WithOne()
               .HasForeignKey<ChapterScriptWorkingCopy>(w => w.ChapterId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
               .WithMany()
               .HasForeignKey(w => w.WriterUserId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ScriptVersion>()
               .WithMany()
               .HasForeignKey(w => w.BaseVersionId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(w => w.WriterUserId)
               .HasDatabaseName("ix_chapter_script_working_copies_writer");
    }
}
