using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class ElementConfiguration : IEntityTypeConfiguration<Element>
{
    public void Configure(EntityTypeBuilder<Element> builder)
    {
        builder.ToTable("elements");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(e => e.PanelId)
               .HasColumnName("panel_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(e => e.ElementType)
               .HasColumnName("element_type")
               .HasColumnType("varchar(20)")
               .HasConversion(
                   v => v.ToString(),
                   v => Enum.Parse<ElementType>(v))
               .IsRequired();

        builder.Property(e => e.ScriptLineId)
               .HasColumnName("script_line_id")
               .HasColumnType("uuid");

        builder.Property(e => e.SpeakerCharacterId)
               .HasColumnName("speaker_character_id")
               .HasColumnType("uuid");

        builder.Property(e => e.ZIndex)
               .HasColumnName("z_index")
               .HasColumnType("int")
               .HasDefaultValue(0)
               .IsRequired();

        builder.Property(e => e.TransformGeometry)
               .HasColumnName("transform_geometry")
               .HasColumnType("jsonb")
               .HasDefaultValueSql("'{}'::jsonb")
               .IsRequired();

        builder.Property(e => e.Content)
               .HasColumnName("content")
               .HasColumnType("text");

        builder.Property(e => e.StyleProperties)
               .HasColumnName("style_properties")
               .HasColumnType("jsonb")
               .HasDefaultValueSql("'{}'::jsonb")
               .IsRequired();

        builder.HasIndex(e => new { e.PanelId, e.ZIndex })
               .HasDatabaseName("ix_elements_panel_zindex");

        builder.HasOne(e => e.ScriptLine)
               .WithMany(l => l.BoundElements)
               .HasForeignKey(e => e.ScriptLineId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}
