using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Bible;

namespace PanelForge.Infrastructure.Persistence.Configurations.Bible;

internal sealed class BibleEntryNameConfiguration : IEntityTypeConfiguration<BibleEntryName>
{
    public void Configure(EntityTypeBuilder<BibleEntryName> builder)
    {
        builder.ToTable("bible_entry_names");

        builder.HasKey(n => new { n.SeriesBibleId, n.NormalizedName });

        builder.Property(n => n.SeriesBibleId)
               .HasColumnName("series_bible_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(n => n.NormalizedName)
               .HasColumnName("normalized_name")
               .HasColumnType("varchar(200)")
               .IsRequired();

        builder.Property(n => n.BibleEntryId)
               .HasColumnName("bible_entry_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(n => n.IsPrimary)
               .HasColumnName("is_primary")
               .HasColumnType("boolean")
               .IsRequired();

        builder.HasIndex(n => n.BibleEntryId)
               .HasDatabaseName("ix_bible_entry_names_entry");

        builder.HasOne(n => n.SeriesBible)
               .WithMany()
               .HasForeignKey(n => n.SeriesBibleId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(n => n.BibleEntry)
               .WithMany()
               .HasForeignKey(n => n.BibleEntryId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
