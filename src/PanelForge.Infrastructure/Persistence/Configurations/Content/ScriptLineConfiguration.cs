using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class ScriptLineConfiguration : IEntityTypeConfiguration<ScriptLine>
{
    public void Configure(EntityTypeBuilder<ScriptLine> builder)
    {
        builder.ToTable("script_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(l => l.SceneId)
               .HasColumnName("scene_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(l => l.LineOrder)
               .HasColumnName("line_order")
               .HasColumnType("int")
               .IsRequired();

        builder.HasIndex(l => new { l.SceneId, l.LineOrder })
               .HasDatabaseName("ix_script_lines_scene_order");

        builder.Property(l => l.SpeakerCharacterId)
               .HasColumnName("speaker_character_id")
               .HasColumnType("uuid");

        builder.Property(l => l.DialogueText)
               .HasColumnName("dialogue_text")
               .HasColumnType("text");

        builder.Property(l => l.StageDirection)
               .HasColumnName("stage_direction")
               .HasColumnType("text");

        builder.HasMany(l => l.BoundElements)
               .WithOne(e => e.ScriptLine)
               .HasForeignKey(e => e.ScriptLineId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}
