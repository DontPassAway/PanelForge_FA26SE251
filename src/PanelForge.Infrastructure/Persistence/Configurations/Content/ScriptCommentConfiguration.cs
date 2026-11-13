using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class ScriptCommentConfiguration : IEntityTypeConfiguration<ScriptComment>
{
    public void Configure(EntityTypeBuilder<ScriptComment> builder)
    {
        builder.ToTable("script_comments");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();

        builder.Property(c => c.ChapterId).HasColumnName("chapter_id").HasColumnType("uuid").IsRequired();
        builder.HasIndex(c => c.ChapterId).HasDatabaseName("ix_script_comments_chapter_id");
        builder.HasOne<Chapter>().WithMany().HasForeignKey(c => c.ChapterId).OnDelete(DeleteBehavior.Cascade);

        builder.Property(c => c.SceneId).HasColumnName("scene_id").HasColumnType("uuid");
        builder.Property(c => c.ScriptLineId).HasColumnName("script_line_id").HasColumnType("uuid");
        builder.Property(c => c.QuotedText).HasColumnName("quoted_text").HasColumnType("text");
        builder.Property(c => c.Body)
               .HasColumnName("body")
               .HasColumnType($"varchar({ScriptComment.MaxBodyLength})")
               .IsRequired();
        builder.Property(c => c.AuthorUserId).HasColumnName("author_user_id").HasColumnType("uuid").IsRequired();
        builder.Property(c => c.ResolvedAt).HasColumnName("resolved_at").HasColumnType("timestamptz");
        builder.Property(c => c.ResolvedById).HasColumnName("resolved_by_id").HasColumnType("uuid");

        builder.Property(c => c.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Ignore(c => c.IsResolved);
    }
}
