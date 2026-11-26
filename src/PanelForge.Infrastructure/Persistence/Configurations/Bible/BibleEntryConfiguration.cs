using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Bible;

internal sealed class BibleEntryConfiguration : IEntityTypeConfiguration<BibleEntry>
{
    private static readonly ValueComparer<List<string>> AliasesComparer = new(
        (c1, c2) => (c1 == null && c2 == null) || (c1 != null && c2 != null && c1.SequenceEqual(c2)),
        c => c == null ? 0 : c.Aggregate(0, (a, v) => HashCode.Combine(a, StringComparer.Ordinal.GetHashCode(v))),
        c => c == null ? new List<string>() : c.ToList());

    public void Configure(EntityTypeBuilder<BibleEntry> builder)
    {
        builder.ToTable("bible_entries");
        builder.Property(x => x.RowVersion)
               .HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(e => e.SeriesBibleId)
               .HasColumnName("series_bible_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(e => e.SeriesBibleId)
               .HasDatabaseName("ix_bible_entries_series_bible_id");

        builder.Property(e => e.Category)
               .HasColumnName("category")
               .HasColumnType("varchar(30)")
               .HasConversion(
                   v => v.ToString(),
                   v => Enum.Parse<BibleEntryCategory>(v))
               .IsRequired();
        builder.HasIndex(e => e.Category)
               .HasDatabaseName("ix_bible_entries_category");

        builder.Property(e => e.Code)
               .HasColumnName("code")
               .HasColumnType("varchar(50)")
               .IsRequired();
        builder.HasIndex(e => new { e.SeriesBibleId, e.Code })
               .IsUnique()
               .HasDatabaseName("ix_bible_entries_series_bible_id_code");

        builder.Property(e => e.Name)
               .HasColumnName("name")
               .HasColumnType("varchar(255)")
               .IsRequired();

        builder.Property(e => e.NormalizedName)
               .HasColumnName("normalized_name")
               .HasColumnType("varchar(200)")
               .IsRequired();
        builder.HasIndex(e => new { e.SeriesBibleId, e.NormalizedName })
               .IsUnique()
               .HasDatabaseName("ix_bible_entries_series_bible_id_normalized_name");

        builder.Property(e => e.Subtitle)
               .HasColumnName("subtitle")
               .HasColumnType("varchar(255)");

        builder.Property(e => e.Description)
               .HasColumnName("description")
               .HasColumnType("text")
               .IsRequired();

        builder.Property(e => e.DetailsJson)
               .HasColumnName("details_json")
               .HasColumnType("jsonb")
               .HasDefaultValueSql("'{}'::jsonb")
               .IsRequired();

        builder.Property(e => e.ReferenceImageUrl)
               .HasColumnName("reference_image_url")
               .HasColumnType("text");

        builder.Property(e => e.Priority)
               .HasColumnName("priority")
               .HasColumnType("varchar(20)")
               .HasConversion(
                   v => v.ToString(),
                   v => Enum.Parse<BiblePriority>(v))
               .IsRequired();

        builder.Property(e => e.StrictCheck)
               .HasColumnName("strict_check")
               .HasColumnType("boolean")
               .HasDefaultValue(true)
               .IsRequired();

        builder.Property(e => e.IsActive)
               .HasColumnName("is_active")
               .HasColumnType("boolean")
               .HasDefaultValue(true)
               .IsRequired();

        builder.Property(e => e.IsStub)
               .HasColumnName("is_stub")
               .HasColumnType("boolean")
               .HasDefaultValue(false)
               .IsRequired();

        builder.Property(e => e.IsCrowd)
               .HasColumnName("is_crowd")
               .HasColumnType("boolean")
               .HasDefaultValue(false)
               .IsRequired();

        var aliasesProp = builder.Property(e => e.Aliases)
               .HasColumnName("aliases")
               .HasColumnType("jsonb")
               .HasDefaultValueSql("'[]'::jsonb")
               .HasConversion(
                   v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                   v => string.IsNullOrWhiteSpace(v)
                       ? new List<string>()
                       : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
               .IsRequired();
        aliasesProp.Metadata.SetValueComparer(AliasesComparer);

        builder.Property(e => e.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(e => e.UpdatedAt)
               .HasColumnName("updated_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.HasMany(e => e.Revisions)
               .WithOne(r => r.BibleEntry)
               .HasForeignKey(r => r.BibleEntryId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
