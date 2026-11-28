using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Workspaces;

internal sealed class ResourcePolicyConfiguration : IEntityTypeConfiguration<ResourcePolicy>
{
    public void Configure(EntityTypeBuilder<ResourcePolicy> builder)
    {
        builder.ToTable("resource_policies");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.RowVersion)
               .HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
    }
}

internal sealed class ResourceRequestConfiguration : IEntityTypeConfiguration<ResourceRequest>
{
    public void Configure(EntityTypeBuilder<ResourceRequest> builder)
    {
        builder.ToTable("resource_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.ResourceType).HasColumnType("varchar(20)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<ResourceType>(v)).IsRequired();
        builder.Property(r => r.Status).HasColumnType("varchar(20)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<ResourceRequestStatus>(v)).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(ResourceRequest.MaxReasonLength).IsRequired();
        builder.Property(r => r.ReviewNote).HasMaxLength(ResourceRequest.MaxReasonLength);
        builder.Property(r => r.RowVersion)
               .HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

        builder.HasIndex(r => new { r.WorkspaceId, r.Status });
        builder.HasOne<StudioWorkspace>().WithMany().HasForeignKey(r => r.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}
