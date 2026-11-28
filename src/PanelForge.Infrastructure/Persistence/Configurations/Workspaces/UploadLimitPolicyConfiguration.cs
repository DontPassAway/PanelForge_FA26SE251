using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workspaces;

namespace PanelForge.Infrastructure.Persistence.Configurations.Workspaces;

internal sealed class UploadLimitPolicyConfiguration : IEntityTypeConfiguration<UploadLimitPolicy>
{
    public void Configure(EntityTypeBuilder<UploadLimitPolicy> builder)
    {
        builder.ToTable("upload_limit_policies", t =>
        {
            t.HasCheckConstraint("ck_upload_limit_policies_artwork",
                $"artwork_max_bytes > 0 AND artwork_max_bytes <= {UploadLimitPolicy.ArtworkCeilingBytes}");
            t.HasCheckConstraint("ck_upload_limit_policies_reference_image",
                $"reference_image_max_bytes > 0 AND reference_image_max_bytes <= {UploadLimitPolicy.ImageCeilingBytes}");
            t.HasCheckConstraint("ck_upload_limit_policies_cover_image",
                $"cover_image_max_bytes > 0 AND cover_image_max_bytes <= {UploadLimitPolicy.ImageCeilingBytes}");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(p => p.ArtworkMaxBytes).HasColumnName("artwork_max_bytes").HasColumnType("bigint").IsRequired();
        builder.Property(p => p.ReferenceImageMaxBytes).HasColumnName("reference_image_max_bytes").HasColumnType("bigint").IsRequired();
        builder.Property(p => p.CoverImageMaxBytes).HasColumnName("cover_image_max_bytes").HasColumnType("bigint").IsRequired();
        builder.Property(p => p.ChangedByUserId).HasColumnName("changed_by_user_id").HasColumnType("uuid").IsRequired();
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");

        builder.HasIndex(p => p.CreatedAt).HasDatabaseName("ix_upload_limit_policies_created_at");

        builder.HasOne<User>().WithMany().HasForeignKey(p => p.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
