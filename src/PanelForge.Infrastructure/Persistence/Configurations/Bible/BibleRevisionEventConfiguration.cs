using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Bible;

internal sealed class BibleRevisionEventConfiguration : IEntityTypeConfiguration<BibleRevisionEvent>
{
    public void Configure(EntityTypeBuilder<BibleRevisionEvent> builder)
    {
        builder.ToTable("bible_revision_events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(e => e.BibleEntryRevisionId)
               .HasColumnName("bible_entry_revision_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(e => new { e.BibleEntryRevisionId, e.CreatedAt })
               .HasDatabaseName("ix_bible_revision_events_revision_created");

        builder.Property(e => e.Type)
               .HasColumnName("event_type")
               .HasColumnType("varchar(30)")
               .HasConversion(
                   v => v.ToString(),
                   v => Enum.Parse<BibleRevisionEventType>(v))
               .IsRequired();

        builder.Property(e => e.ActorId)
               .HasColumnName("actor_id")
               .HasColumnType("uuid");

        builder.Property(e => e.Comment)
               .HasColumnName("comment")
               .HasColumnType("varchar(1000)");

        builder.Property(e => e.ImageUrl)
               .HasColumnName("image_url")
               .HasColumnType("text");

        builder.Property(e => e.EventHash)
               .HasColumnName("event_hash")
               .HasColumnType("varchar(64)")
               .IsRequired();

        builder.Property(e => e.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.HasOne<BibleEntryRevision>()
               .WithMany(r => r.Events)
               .HasForeignKey(e => e.BibleEntryRevisionId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
