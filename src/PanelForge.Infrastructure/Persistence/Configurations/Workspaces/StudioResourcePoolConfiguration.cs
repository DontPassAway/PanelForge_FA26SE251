using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workspaces;

namespace PanelForge.Infrastructure.Persistence.Configurations.Workspaces;

internal sealed class StudioResourcePoolConfiguration : IEntityTypeConfiguration<StudioResourcePool>
{
    public void Configure(EntityTypeBuilder<StudioResourcePool> builder)
    {
        builder.ToTable("studio_resource_pools", t =>
        {
            t.HasCheckConstraint("ck_studio_resource_pools_storage_non_negative", "storage_bytes >= 0");
            t.HasCheckConstraint("ck_studio_resource_pools_tokens_non_negative", "tokens >= 0");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(p => p.RowVersion)
               .HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

        builder.Property(p => p.OwnerId).HasColumnName("owner_id").HasColumnType("uuid").IsRequired();
        builder.HasIndex(p => p.OwnerId).IsUnique().HasDatabaseName("ix_studio_resource_pools_owner_id");

        builder.Property(p => p.StorageBytes).HasColumnName("storage_bytes").HasColumnType("bigint").IsRequired();
        builder.Property(p => p.Tokens).HasColumnName("tokens").HasColumnType("bigint").IsRequired();

        builder.Property(p => p.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
        builder.Property(p => p.IsDeleted).HasColumnName("is_deleted").HasColumnType("boolean").HasDefaultValue(false).IsRequired();

        builder.HasOne<User>().WithMany().HasForeignKey(p => p.OwnerId).OnDelete(DeleteBehavior.Restrict);
    }
}
