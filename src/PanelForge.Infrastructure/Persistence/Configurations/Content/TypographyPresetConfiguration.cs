using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class TypographyPresetConfiguration : IEntityTypeConfiguration<TypographyPreset>
{
    public void Configure(EntityTypeBuilder<TypographyPreset> builder)
    {
        builder.ToTable("typography_presets");

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
               .HasDatabaseName("ix_typography_presets_series_id");

        builder.Property(p => p.Name)
               .HasColumnName("name")
               .HasColumnType("varchar(200)")
               .IsRequired();

        builder.Property(p => p.FontFamily)
               .HasColumnName("font_family")
               .HasColumnType("varchar(200)")
               .IsRequired();

        builder.Property(p => p.FontSize)
               .HasColumnName("font_size")
               .IsRequired();

        builder.Property(p => p.FontWeight)
               .HasColumnName("font_weight")
               .HasDefaultValue(400)
               .IsRequired();

        builder.Property(p => p.FontStyle)
               .HasColumnName("font_style")
               .HasColumnType("varchar(50)")
               .HasDefaultValue("Normal")
               .IsRequired();

        builder.Property(p => p.LineHeight)
               .HasColumnName("line_height")
               .HasColumnType("decimal(5,2)")
               .HasDefaultValue(1.2m)
               .IsRequired();

        builder.Property(p => p.LetterSpacing)
               .HasColumnName("letter_spacing")
               .HasColumnType("decimal(5,2)")
               .HasDefaultValue(0m)
               .IsRequired();

        builder.Property(p => p.TextAlign)
               .HasColumnName("text_align")
               .HasColumnType("varchar(30)")
               .HasDefaultValue("Left")
               .IsRequired();

        builder.Property(p => p.UsageType)
               .HasColumnName("usage_type")
               .HasColumnType("varchar(50)")
               .HasDefaultValue("Custom")
               .IsRequired();

        builder.Property(p => p.IsDefault)
               .HasColumnName("is_default")
               .HasDefaultValue(false)
               .IsRequired();

        builder.Property(p => p.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(p => p.UpdatedAt)
               .HasColumnName("updated_at")
               .HasColumnType("timestamptz");

        builder.HasOne(p => p.Series)
               .WithMany(s => s.TypographyPresets)
               .HasForeignKey(p => p.SeriesId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
