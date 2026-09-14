using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class SceneConfiguration : IEntityTypeConfiguration<Scene>
{
    public void Configure(EntityTypeBuilder<Scene> builder)
    {
        builder.ToTable("scenes");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(s => s.ChapterId)
               .HasColumnName("chapter_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(s => s.SceneNumber)
               .HasColumnName("scene_number")
               .HasColumnType("int")
               .IsRequired();

        builder.HasIndex(s => new { s.ChapterId, s.SceneNumber })
               .IsUnique()
               .HasDatabaseName("ix_scenes_chapter_number");

        builder.Property(s => s.Heading)
               .HasColumnName("heading")
               .HasColumnType("varchar(500)");

        builder.Property(s => s.Summary)
               .HasColumnName("summary")
               .HasColumnType("text");

        builder.HasMany(s => s.ScriptLines)
               .WithOne(l => l.Scene)
               .HasForeignKey(l => l.SceneId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(s => s.Pages)
               .WithOne(p => p.Scene)
               .HasForeignKey(p => p.SceneId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}
