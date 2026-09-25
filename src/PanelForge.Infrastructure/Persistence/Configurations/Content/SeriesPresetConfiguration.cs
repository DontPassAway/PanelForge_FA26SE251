using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class SeriesPresetConfiguration : IEntityTypeConfiguration<SeriesPreset>
{
    public void Configure(EntityTypeBuilder<SeriesPreset> builder)
    {
        builder.ToTable("series_presets");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(p => p.SeriesId)
               .HasColumnName("series_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(p => p.SeriesId)
               .IsUnique()
               .HasDatabaseName("ix_series_presets_series_id");

        builder.Property(p => p.TypographyPresetsJson)
               .HasColumnName("typography_presets_json")
               .HasColumnType("text")
               .IsRequired();

        builder.Property(p => p.ConsistencyRulesJson)
               .HasColumnName("consistency_rules_json")
               .HasColumnType("text")
               .IsRequired();

        builder.Property(p => p.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(p => p.UpdatedAt)
               .HasColumnName("updated_at")
               .HasColumnType("timestamptz");

        builder.HasOne(p => p.Series)
               .WithOne(s => s.Preset)
               .HasForeignKey<SeriesPreset>(p => p.SeriesId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
