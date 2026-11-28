using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Payments;

namespace PanelForge.Infrastructure.Persistence.Configurations.Payments;

internal sealed class StudioResourcePriceConfiguration : IEntityTypeConfiguration<StudioResourcePrice>
{
    public void Configure(EntityTypeBuilder<StudioResourcePrice> builder)
    {
        builder.ToTable("studio_resource_prices", t =>
        {
            t.HasCheckConstraint("ck_studio_resource_prices_gb_positive", "price_per_gb_vnd > 0");
            t.HasCheckConstraint("ck_studio_resource_prices_tokens_positive", "price_per_million_tokens_vnd > 0");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(p => p.PricePerGbVnd).HasColumnName("price_per_gb_vnd").HasColumnType("integer").IsRequired();
        builder.Property(p => p.PricePerMillionTokensVnd).HasColumnName("price_per_million_tokens_vnd").HasColumnType("integer").IsRequired();
        builder.Property(p => p.ChangedByUserId).HasColumnName("changed_by_user_id").HasColumnType("uuid").IsRequired();

        builder.Property(p => p.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(p => p.IsDeleted).HasColumnName("is_deleted").HasColumnType("boolean").HasDefaultValue(false).IsRequired();

        builder.HasIndex(p => p.CreatedAt).HasDatabaseName("ix_studio_resource_prices_created_at");

        builder.HasOne<PanelForge.Domain.Entities.Identity.User>()
               .WithMany()
               .HasForeignKey(p => p.ChangedByUserId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
