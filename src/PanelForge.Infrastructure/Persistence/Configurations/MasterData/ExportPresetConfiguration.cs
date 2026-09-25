using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.MasterData;

namespace PanelForge.Infrastructure.Persistence.Configurations.MasterData;

internal sealed class ExportPresetConfiguration : IEntityTypeConfiguration<ExportPreset>
{
    public void Configure(EntityTypeBuilder<ExportPreset> builder)
    {
        builder.ToTable("export_presets");

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
               .HasDatabaseName("ix_export_presets_code");

        builder.Property(e => e.Name)
               .HasColumnName("name")
               .HasColumnType("varchar(200)")
               .IsRequired();

        builder.Property(e => e.FormatName)
               .HasColumnName("format_name")
               .HasColumnType("varchar(100)")
               .IsRequired();

        builder.Property(e => e.ConfigOptionsJson)
               .HasColumnName("config_options_json")
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
