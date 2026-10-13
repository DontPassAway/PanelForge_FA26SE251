using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Preview;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Preview;

internal sealed class PreviewLinkConfiguration : IEntityTypeConfiguration<PreviewLink>
{
    public void Configure(EntityTypeBuilder<PreviewLink> builder)
    {
        builder.ToTable("preview_links");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(l => l.ChapterId).HasColumnName("chapter_id").HasColumnType("uuid").IsRequired();
        builder.HasIndex(l => l.ChapterId).HasDatabaseName("ix_preview_links_chapter_id");
        builder.Property(l => l.WorkspaceId).HasColumnName("workspace_id").HasColumnType("uuid").IsRequired();
        builder.Property(l => l.RecipientEmail).HasColumnName("recipient_email").HasColumnType("varchar(320)").IsRequired();
        builder.Property(l => l.TokenHash).HasColumnName("token_hash").HasColumnType("varchar(64)").IsRequired();
        builder.HasIndex(l => l.TokenHash).IsUnique().HasDatabaseName("ux_preview_links_token_hash");
        builder.Property(l => l.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(l => l.RevokedAt).HasColumnName("revoked_at").HasColumnType("timestamptz");
        builder.Property(l => l.RevokedByUserId).HasColumnName("revoked_by_user_id").HasColumnType("uuid");
        builder.Property(l => l.RevokeReason).HasColumnName("revoke_reason").HasColumnType("varchar(200)");
        builder.Property(l => l.CreatedByUserId).HasColumnName("created_by_user_id").HasColumnType("uuid").IsRequired();
        builder.Property(l => l.SnapshotArtworkIds).HasColumnName("snapshot_artwork_ids").HasColumnType("uuid[]").IsRequired();
        builder.Property(l => l.ChapterVersion).HasColumnName("chapter_version").HasColumnType("bigint").IsRequired();
        builder.Property(l => l.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
        builder.HasOne<Chapter>().WithMany().HasForeignKey(l => l.ChapterId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PreviewLinkAccessLogConfiguration : IEntityTypeConfiguration<PreviewLinkAccessLog>
{
    public void Configure(EntityTypeBuilder<PreviewLinkAccessLog> builder)
    {
        builder.ToTable("preview_link_access_logs");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(l => l.PreviewLinkId).HasColumnName("preview_link_id").HasColumnType("uuid");
        builder.HasIndex(l => l.PreviewLinkId).HasDatabaseName("ix_preview_link_access_logs_link_id");
        builder.Property(l => l.Action).HasColumnName("action").HasColumnType("varchar(20)").IsRequired();
        builder.Property(l => l.Allowed).HasColumnName("allowed").HasColumnType("boolean").IsRequired();
        builder.Property(l => l.DenyReason).HasColumnName("deny_reason").HasColumnType("varchar(40)");
        builder.Property(l => l.IpAddress).HasColumnName("ip_address").HasColumnType("varchar(64)");
        builder.Property(l => l.AccessedAt).HasColumnName("accessed_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(l => l.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
        builder.HasOne<PreviewLink>().WithMany().HasForeignKey(l => l.PreviewLinkId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PreviewFeedbackConfiguration : IEntityTypeConfiguration<PreviewFeedback>
{
    public void Configure(EntityTypeBuilder<PreviewFeedback> builder)
    {
        builder.ToTable("preview_feedback");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(f => f.PreviewLinkId).HasColumnName("preview_link_id").HasColumnType("uuid").IsRequired();
        builder.Property(f => f.ChapterId).HasColumnName("chapter_id").HasColumnType("uuid").IsRequired();
        builder.HasIndex(f => new { f.ChapterId, f.Status }).HasDatabaseName("ix_preview_feedback_chapter_status");
        builder.Property(f => f.PageId).HasColumnName("page_id").HasColumnType("uuid");
        builder.Property(f => f.PanelId).HasColumnName("panel_id").HasColumnType("uuid");
        builder.Property(f => f.AssetReferenceId).HasColumnName("asset_reference_id").HasColumnType("uuid");
        builder.Property(f => f.ChapterVersion).HasColumnName("chapter_version").HasColumnType("bigint").IsRequired();
        builder.Property(f => f.RecipientEmail).HasColumnName("recipient_email").HasColumnType("varchar(320)").IsRequired();
        builder.Property(f => f.Message).HasColumnName("message").HasColumnType("text").IsRequired();
        builder.Property(f => f.Status).HasColumnName("status").HasColumnType("varchar(20)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<PreviewFeedbackStatus>(v)).IsRequired();
        builder.Property(f => f.TriageNote).HasColumnName("triage_note").HasColumnType("text");
        builder.Property(f => f.TriagedByUserId).HasColumnName("triaged_by_user_id").HasColumnType("uuid");
        builder.Property(f => f.TriagedAt).HasColumnName("triaged_at").HasColumnType("timestamptz");
        builder.Property(f => f.ChangeRequestId).HasColumnName("change_request_id").HasColumnType("uuid");
        builder.Property(f => f.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(f => f.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
        builder.HasOne<PreviewLink>().WithMany().HasForeignKey(f => f.PreviewLinkId).OnDelete(DeleteBehavior.Cascade);
    }
}
