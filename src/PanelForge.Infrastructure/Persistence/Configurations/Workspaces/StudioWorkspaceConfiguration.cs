using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Auth;

namespace PanelForge.Infrastructure.Persistence.Configurations.Auth;

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
