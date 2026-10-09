using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class ChapterReviewConfiguration : IEntityTypeConfiguration<ChapterReview>
{
    public void Configure(EntityTypeBuilder<ChapterReview> builder)
    {
        builder.ToTable("chapter_reviews");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();

        builder.Property(r => r.ChapterId).HasColumnName("chapter_id").HasColumnType("uuid").IsRequired();
        builder.HasIndex(r => r.ChapterId).HasDatabaseName("ix_chapter_reviews_chapter_id");

        builder.Property(r => r.Action)
               .HasColumnName("action")
               .HasColumnType("varchar(30)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<ChapterReviewAction>(v))
               .IsRequired();

        builder.Property(r => r.ActorUserId).HasColumnName("actor_user_id").HasColumnType("uuid").IsRequired();
        builder.Property(r => r.Comment).HasColumnName("comment").HasColumnType("text");
        builder.Property(r => r.ScheduledAt).HasColumnName("scheduled_at").HasColumnType("timestamptz");
        builder.Property(r => r.ChapterVersion).HasColumnName("chapter_version").HasColumnType("bigint").IsRequired();
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
    }
}
