using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Auth;

namespace PanelForge.Infrastructure.Persistence.Configurations.Auth;

internal sealed class ExternalPreviewLinkConfiguration : IEntityTypeConfiguration<ExternalPreviewLink>
{
    public void Configure(EntityTypeBuilder<ExternalPreviewLink> builder)
    {
        builder.ToTable("external_preview_links");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(l => l.ChapterId)
               .HasColumnName("chapter_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(l => l.ChapterId)
               .HasDatabaseName("ix_external_preview_links_chapter_id");

        builder.Property(l => l.TokenHash)
               .HasColumnName("token_hash")
               .HasColumnType("varchar(256)")
               .IsRequired();
        builder.HasIndex(l => l.TokenHash)
               .IsUnique()
               .HasDatabaseName("ix_external_preview_links_token_hash");

        builder.Property(l => l.WatermarkText)
               .HasColumnName("watermark_text")
               .HasColumnType("varchar(512)")
               .IsRequired();

        builder.Property(l => l.ExpiresAt)
               .HasColumnName("expires_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(l => l.IsRevoked)
               .HasColumnName("is_revoked")
               .HasDefaultValue(false)
               .IsRequired();

        builder.Property(l => l.CreatedByUserId)
               .HasColumnName("created_by_user_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(l => l.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.HasOne(l => l.CreatedByUser)
               .WithMany(u => u.CreatedPreviewLinks)
               .HasForeignKey(l => l.CreatedByUserId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
