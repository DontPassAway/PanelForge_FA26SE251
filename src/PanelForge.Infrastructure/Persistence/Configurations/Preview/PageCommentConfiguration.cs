using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Preview;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Preview;

internal sealed class PageCommentConfiguration : IEntityTypeConfiguration<PageComment>
{
    public void Configure(EntityTypeBuilder<PageComment> builder)
    {
        builder.ToTable("page_comments");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(c => c.SeriesId).HasColumnName("series_id").HasColumnType("uuid").IsRequired();
        builder.Property(c => c.ChapterId).HasColumnName("chapter_id").HasColumnType("uuid").IsRequired();
        builder.Property(c => c.PageId).HasColumnName("page_id").HasColumnType("uuid");
        builder.Property(c => c.PanelId).HasColumnName("panel_id").HasColumnType("uuid");
        builder.Property(c => c.AssetReferenceId).HasColumnName("asset_reference_id").HasColumnType("uuid");
        builder.Property(c => c.PinX).HasColumnName("pin_x").HasColumnType("double precision");
        builder.Property(c => c.PinY).HasColumnName("pin_y").HasColumnType("double precision");
        builder.Property(c => c.Message).HasColumnName("message").HasColumnType("text").IsRequired();
        builder.Property(c => c.AuthorUserId).HasColumnName("author_user_id").HasColumnType("uuid").IsRequired();
        builder.Property(c => c.Status).HasColumnName("status").HasColumnType("varchar(30)")
            .HasConversion(v => v.ToString(), v => Enum.Parse<PageCommentStatus>(v)).IsRequired();
        builder.Property(c => c.ResolutionNote).HasColumnName("resolution_note").HasColumnType("text");
        builder.Property(c => c.ResolvedByUserId).HasColumnName("resolved_by_user_id").HasColumnType("uuid");
        builder.Property(c => c.ResolvedAt).HasColumnName("resolved_at").HasColumnType("timestamptz");
        builder.Property(c => c.StageTaskId).HasColumnName("stage_task_id").HasColumnType("uuid");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");

        builder.HasIndex(c => new { c.ChapterId, c.Status }).HasDatabaseName("ix_page_comments_chapter_status");
        builder.HasIndex(c => c.SeriesId).HasDatabaseName("ix_page_comments_series_id");

        builder.HasOne<Series>().WithMany().HasForeignKey(c => c.SeriesId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Chapter>().WithMany().HasForeignKey(c => c.ChapterId).OnDelete(DeleteBehavior.Cascade);
    }
}
