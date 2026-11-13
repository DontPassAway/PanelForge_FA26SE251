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

        builder.Property(l => l.PanelId)
               .HasColumnName("panel_id")
               .HasColumnType("uuid");

        builder.HasIndex(l => l.PanelId)
               .HasDatabaseName("ix_script_lines_panel");

        // Panel bị xoá thì dòng thoại ở lại Scene, chỉ mất neo (ScriptLine thuộc tự sự, không thuộc bố cục)
        builder.HasOne(l => l.Panel)
               .WithMany(p => p.ScriptLines)
               .HasForeignKey(l => l.PanelId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.Property(l => l.Kind)
               .HasColumnName("kind")
               .HasColumnType("varchar(30)")
               .HasConversion(
                   v => v.ToString(),
                   v => Enum.Parse<PanelForge.Domain.Enums.ScriptLineKind>(v))
               .HasDefaultValue(PanelForge.Domain.Enums.ScriptLineKind.Action)
               .IsRequired();

        builder.Property(l => l.Text)
               .HasColumnName("text")
               .HasColumnType("text");

        builder.Property(l => l.SpeakerLabel)
               .HasColumnName("speaker_label")
               .HasColumnType("varchar(100)");

        builder.Property(l => l.SpeakerCharacterId)
               .HasColumnName("speaker_character_id")
               .HasColumnType("uuid");
        builder.HasIndex(l => l.SpeakerCharacterId)
               .HasDatabaseName("ix_script_lines_speaker_character_id");

        builder.Property(l => l.DialogueText)
               .HasColumnName("dialogue_text")
               .HasColumnType("text");

        builder.Property(l => l.StageDirection)
               .HasColumnName("stage_direction")
               .HasColumnType("text");

        builder.Property(l => l.NarrationMode)
               .HasColumnName("narration_mode")
               .HasColumnType("varchar(30)")
               .HasConversion(
                   v => v.ToString(),
                   v => Enum.Parse<PanelForge.Domain.Enums.NarrationMode>(v))
               .HasDefaultValue(PanelForge.Domain.Enums.NarrationMode.Present);
    }
}
