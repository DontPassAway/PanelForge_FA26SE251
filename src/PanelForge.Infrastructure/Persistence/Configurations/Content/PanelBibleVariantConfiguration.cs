using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class PanelBibleVariantConfiguration : IEntityTypeConfiguration<PanelBibleVariant>
{
    public void Configure(EntityTypeBuilder<PanelBibleVariant> builder)
    {
        builder.ToTable("panel_bible_variants");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(p => p.PanelId)
               .HasColumnName("panel_id")
               .HasColumnType("uuid");

        builder.HasIndex(p => new { p.PanelId, p.BibleEntryId })
               .IsUnique()
               .HasDatabaseName("ix_panel_bible_variants_panel_entry");

        builder.Property(p => p.BibleEntryId)
               .HasColumnName("bible_entry_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(p => p.RevisionId)
               .HasColumnName("revision_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(p => p.SetById)
               .HasColumnName("set_by")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(p => p.SetAt)
               .HasColumnName("set_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.HasOne(p => p.Panel)
               .WithMany()
               .HasForeignKey(p => p.PanelId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.BibleEntry)
               .WithMany()
               .HasForeignKey(p => p.BibleEntryId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Revision)
               .WithMany()
               .HasForeignKey(p => p.RevisionId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.SetBy)
               .WithMany()
               .HasForeignKey(p => p.SetById)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
