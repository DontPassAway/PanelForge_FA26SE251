using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Bible;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class SeriesBibleConfiguration : IEntityTypeConfiguration<SeriesBible>
{
    public void Configure(EntityTypeBuilder<SeriesBible> builder)
    {
        builder.ToTable("series_bibles");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(b => b.SeriesId)
               .HasColumnName("series_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(b => b.SeriesId)
               .IsUnique()
               .HasDatabaseName("ix_series_bibles_series_id");

        builder.Property(b => b.WorkspaceId)
               .HasColumnName("workspace_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(b => b.WorkspaceId)
               .HasDatabaseName("ix_series_bibles_workspace_id");

        builder.Property(b => b.Name)
               .HasColumnName("name")
               .HasColumnType("varchar(255)")
               .IsRequired();

        builder.Property(b => b.Description)
               .HasColumnName("description")
               .HasColumnType("text");

        builder.Property(b => b.Version)
               .HasColumnName("version")
               .HasColumnType("integer")
               .HasDefaultValue(1)
               .IsRequired();

        builder.Property(b => b.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(b => b.UpdatedAt)
               .HasColumnName("updated_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.HasOne(b => b.Series)
               .WithOne(s => s.Bible)
               .HasForeignKey<SeriesBible>(b => b.SeriesId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(b => b.Workspace)
               .WithMany()
               .HasForeignKey(b => b.WorkspaceId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(b => b.Entries)
               .WithOne(e => e.SeriesBible)
               .HasForeignKey(e => e.SeriesBibleId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
