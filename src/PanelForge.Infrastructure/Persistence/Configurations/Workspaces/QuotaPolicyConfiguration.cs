using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workspaces;

namespace PanelForge.Infrastructure.Persistence.Configurations.Workspaces;

internal sealed class QuotaPolicyConfiguration : IEntityTypeConfiguration<QuotaPolicy>
{
    public void Configure(EntityTypeBuilder<QuotaPolicy> builder)
    {
        builder.ToTable("quota_policies", t =>
        {
            t.HasCheckConstraint("ck_quota_policies_storage_positive", "storage_quota_bytes > 0");
            t.HasCheckConstraint("ck_quota_policies_tokens_positive", "monthly_token_quota > 0");
            t.HasCheckConstraint("ck_quota_policies_images_positive", "monthly_image_quota > 0");
            t.HasCheckConstraint("ck_quota_policies_requests_positive", "monthly_request_quota > 0");
            t.HasCheckConstraint("ck_quota_policies_requests_per_minute_positive", "requests_per_minute > 0");
            t.HasCheckConstraint("ck_quota_policies_warning_threshold", "warning_threshold_percent BETWEEN 1 AND 99");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(p => p.StorageQuotaBytes).HasColumnName("storage_quota_bytes").HasColumnType("bigint").IsRequired();
        builder.Property(p => p.MonthlyTokenQuota).HasColumnName("monthly_token_quota").HasColumnType("bigint").IsRequired();
        builder.Property(p => p.MonthlyImageQuota).HasColumnName("monthly_image_quota").HasColumnType("bigint").IsRequired();
        builder.Property(p => p.MonthlyRequestQuota).HasColumnName("monthly_request_quota").HasColumnType("bigint")
            .HasDefaultValue(QuotaPolicy.DefaultMonthlyRequestQuota).IsRequired();
        builder.Property(p => p.RequestsPerMinute).HasColumnName("requests_per_minute")
            .HasDefaultValue(QuotaPolicy.DefaultRequestsPerMinute).IsRequired();
        builder.Property(p => p.AiEnabledByDefault).HasColumnName("ai_enabled_by_default").HasDefaultValue(true).IsRequired();
        builder.Property(p => p.WarningThresholdPercent).HasColumnName("warning_threshold_percent")
            .HasDefaultValue(QuotaPolicy.DefaultWarningThresholdPercent).IsRequired();
        builder.Property(p => p.ChangedByUserId).HasColumnName("changed_by_user_id").HasColumnType("uuid").IsRequired();
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");

        // Bản mới nhất là bản đang dùng
        builder.HasIndex(p => p.CreatedAt).HasDatabaseName("ix_quota_policies_created_at");

        builder.HasOne<User>().WithMany().HasForeignKey(p => p.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
