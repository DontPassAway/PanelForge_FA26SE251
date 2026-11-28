using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Payments;

namespace PanelForge.Infrastructure.Persistence.Configurations.Payments;

internal sealed class StudioPackagePriceConfiguration : IEntityTypeConfiguration<StudioPackagePrice>
{
    public void Configure(EntityTypeBuilder<StudioPackagePrice> builder)
    {
        builder.ToTable("studio_package_prices", t =>
            t.HasCheckConstraint("ck_studio_package_prices_price_positive", "price_vnd > 0"));

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(p => p.PriceVnd).HasColumnName("price_vnd").HasColumnType("integer").IsRequired();
        builder.Property(p => p.ChangedByUserId).HasColumnName("changed_by_user_id").HasColumnType("uuid").IsRequired();

        builder.Property(p => p.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(p => p.IsDeleted).HasColumnName("is_deleted").HasColumnType("boolean").HasDefaultValue(false).IsRequired();

        // Giá đang bán = bản ghi mới nhất
        builder.HasIndex(p => p.CreatedAt).HasDatabaseName("ix_studio_package_prices_created_at");

        builder.HasOne<PanelForge.Domain.Entities.Identity.User>()
               .WithMany()
               .HasForeignKey(p => p.ChangedByUserId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
