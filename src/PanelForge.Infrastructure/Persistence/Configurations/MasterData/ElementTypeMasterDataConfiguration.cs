using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.MasterData;

namespace PanelForge.Infrastructure.Persistence.Configurations.MasterData;

internal sealed class ElementTypeMasterDataConfiguration : IEntityTypeConfiguration<ElementTypeMasterData>
{
    public void Configure(EntityTypeBuilder<ElementTypeMasterData> builder)
    {
        builder.ToTable("element_types");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(e => e.Code)
               .HasColumnName("code")
               .HasColumnType("varchar(100)")
               .IsRequired();
        builder.HasIndex(e => e.Code)
               .IsUnique()
               .HasDatabaseName("ix_element_types_code");

        builder.Property(e => e.Name)
               .HasColumnName("name")
               .HasColumnType("varchar(200)")
               .IsRequired();

        builder.Property(e => e.Description)
               .HasColumnName("description")
               .HasColumnType("text");

        builder.Property(e => e.AllowedPropertiesJson)
               .HasColumnName("allowed_properties_json")
               .HasColumnType("jsonb")
               .HasDefaultValueSql("'{}'::jsonb")
               .IsRequired();

        builder.Property(e => e.IsActive)
               .HasColumnName("is_active")
               .HasDefaultValue(true)
               .IsRequired();

        builder.Property(e => e.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(e => e.UpdatedAt)
               .HasColumnName("updated_at")
               .HasColumnType("timestamptz");

        builder.Ignore(e => e.IsDeleted);
        builder.Ignore(e => e.DeletedAt);
        builder.Ignore(e => e.DeletedBy);
        builder.Ignore(e => e.CreatedBy);
        builder.Ignore(e => e.UpdatedBy);
    }
}
