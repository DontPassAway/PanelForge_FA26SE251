using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class PageKeepOutZonesConfiguration : IEntityTypeConfiguration<PageKeepOutZones>
{
    public void Configure(EntityTypeBuilder<PageKeepOutZones> builder)
    {
        builder.ToTable("page_keep_out_zones");

        builder.HasKey(z => z.Id);
        builder.Property(z => z.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(z => z.PageId).HasColumnName("page_id").HasColumnType("uuid").IsRequired();
        builder.Property(z => z.ZonesJson).HasColumnName("zones_json").HasColumnType("jsonb").IsRequired();
        builder.Property(z => z.Version).HasColumnName("version").HasColumnType("int").IsConcurrencyToken().IsRequired();
        builder.Property(z => z.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(z => z.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
        builder.Property(z => z.IsDeleted).HasColumnName("is_deleted").HasColumnType("boolean").HasDefaultValue(false).IsRequired();

        builder.HasIndex(z => z.PageId).IsUnique().HasDatabaseName("ix_page_keep_out_zones_page_id");
        builder.HasOne<Page>().WithMany().HasForeignKey(z => z.PageId).OnDelete(DeleteBehavior.Cascade);
    }
}
