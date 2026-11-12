using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Workspaces;

namespace PanelForge.Infrastructure.Persistence.Configurations.Workspaces;

internal sealed class StudioWorkspaceConfiguration : IEntityTypeConfiguration<StudioWorkspace>
{
    public void Configure(EntityTypeBuilder<StudioWorkspace> builder)
    {
        builder.ToTable("studio_workspaces");

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(w => w.Name)
               .HasColumnName("name")
               .HasColumnType("varchar(255)")
               .IsRequired();

        builder.Property(w => w.OwnerId)
               .HasColumnName("owner_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(w => w.OwnerId)
               .HasDatabaseName("ix_studio_workspaces_owner_id");

        builder.Property(w => w.StorageQuotaBytes)
               .HasColumnName("storage_quota_bytes")
               .HasColumnType("bigint")
               .HasDefaultValue(0L)
               .IsRequired();

        builder.Property(w => w.UsedStorageBytes)
               .HasColumnName("used_storage_bytes")
               .HasColumnType("bigint")
               .HasDefaultValue(0L)
               .IsRequired();

        builder.Property(w => w.SessionLifetimeMinutes).HasColumnName("session_lifetime_minutes").HasColumnType("integer");
        builder.Property(w => w.SessionIdleTimeoutMinutes).HasColumnName("session_idle_timeout_minutes").HasColumnType("integer");
        builder.Property(w => w.MaxConcurrentSessions).HasColumnName("max_concurrent_sessions").HasColumnType("integer");

        builder.Property(w => w.CoverImageSha256)
               .HasColumnName("cover_image_sha256")
               .HasColumnType("varchar(64)")
               .IsRequired(false);

        builder.Property(w => w.BankBin)
               .HasColumnName("bank_bin")
               .HasColumnType("varchar(20)");

        builder.Property(w => w.BankName)
               .HasColumnName("bank_name")
               .HasColumnType("varchar(100)");

        builder.Property(w => w.BankAccountNo)
               .HasColumnName("bank_account_no")
               .HasColumnType("varchar(50)");

        builder.Property(w => w.BankAccountName)
               .HasColumnName("bank_account_name")
               .HasColumnType("varchar(200)");

        builder.Property(w => w.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.HasMany(w => w.Members)
               .WithOne(m => m.Workspace)
               .HasForeignKey(m => m.WorkspaceId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(w => w.Series)
               .WithOne(s => s.Workspace)
               .HasForeignKey(s => s.WorkspaceId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
