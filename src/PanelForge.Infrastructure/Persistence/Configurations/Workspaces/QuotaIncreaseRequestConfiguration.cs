using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Workspaces;

internal sealed class QuotaIncreaseRequestConfiguration : IEntityTypeConfiguration<QuotaIncreaseRequest>
{
    public void Configure(EntityTypeBuilder<QuotaIncreaseRequest> builder)
    {
        builder.ToTable("quota_increase_requests", t =>
            t.HasCheckConstraint("ck_quota_increase_requests_requested_gt_current", "requested_quota > current_quota"));

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(r => r.RowVersion)
               .HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

        builder.Property(r => r.WorkspaceId).HasColumnName("workspace_id").HasColumnType("uuid").IsRequired();
        builder.Property(r => r.ResourceType)
               .HasColumnName("resource_type")
               .HasColumnType("varchar(20)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<QuotaResourceType>(v))
               .IsRequired();
        builder.Property(r => r.CurrentQuota).HasColumnName("current_quota").HasColumnType("bigint").IsRequired();
        builder.Property(r => r.RequestedQuota).HasColumnName("requested_quota").HasColumnType("bigint").IsRequired();
        builder.Property(r => r.Reason).HasColumnName("reason").HasColumnType("varchar(1000)").IsRequired();
        // Mặc định đặt trong entity, không dùng HasDefaultValue: CLR default làm EF bỏ cột khi INSERT
        builder.Property(r => r.Status)
               .HasColumnName("status")
               .HasColumnType("varchar(20)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<QuotaIncreaseRequestStatus>(v))
               .IsRequired();
        builder.Property(r => r.RequestedByUserId).HasColumnName("requested_by_user_id").HasColumnType("uuid").IsRequired();
        builder.Property(r => r.ReviewedByUserId).HasColumnName("reviewed_by_user_id").HasColumnType("uuid");
        builder.Property(r => r.ReviewedAt).HasColumnName("reviewed_at").HasColumnType("timestamptz");
        builder.Property(r => r.ReviewNote).HasColumnName("review_note").HasColumnType("varchar(1000)");
        builder.Property(r => r.GrantedQuota).HasColumnName("granted_quota").HasColumnType("bigint");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");

        // Một yêu cầu đang chờ cho mỗi (Studio, loại hạn mức); gửi trùng lúc → vi phạm unique → 409
        builder.HasIndex(r => new { r.WorkspaceId, r.ResourceType })
               .IsUnique()
               .HasFilter("status = 'Pending' AND is_deleted = false")
               .HasDatabaseName("ux_quota_increase_requests_pending");
        builder.HasIndex(r => new { r.Status, r.CreatedAt }).HasDatabaseName("ix_quota_increase_requests_status_created");

        builder.HasOne(r => r.Workspace).WithMany().HasForeignKey(r => r.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
