using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class SeriesConfiguration : IEntityTypeConfiguration<Series>
{
    public void Configure(EntityTypeBuilder<Series> builder)
    {
        builder.ToTable("series");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(s => s.WorkspaceId)
               .HasColumnName("workspace_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(s => s.WorkspaceId)
               .HasDatabaseName("ix_series_workspace_id");

        builder.Property(s => s.Title)
               .HasColumnName("title")
               .HasColumnType("varchar(500)")
               .IsRequired();

        builder.Property(s => s.Synopsis)
               .HasColumnName("synopsis")
               .HasColumnType("text");

        builder.Property(s => s.ReadingDirection)
               .HasColumnName("reading_direction")
               .HasColumnType("varchar(20)")
               .HasConversion(
                   v => v.ToString(),
                   v => Enum.Parse<ReadingDirection>(v))
               .IsRequired();

        builder.Property(s => s.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.HasMany(s => s.Chapters)
               .WithOne(c => c.Series)
               .HasForeignKey(c => c.SeriesId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Bible)
               .WithOne(b => b.Series)
               .HasForeignKey<SeriesBible>(b => b.SeriesId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
