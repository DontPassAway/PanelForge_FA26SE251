using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class ConsistencyRuleConfiguration : IEntityTypeConfiguration<ConsistencyRule>
{
    public void Configure(EntityTypeBuilder<ConsistencyRule> builder)
    {
        builder.ToTable("consistency_rules");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(r => r.SeriesId)
               .HasColumnName("series_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(r => r.SeriesId)
               .HasDatabaseName("ix_consistency_rules_series_id");

        builder.Property(r => r.Name)
               .HasColumnName("name")
               .HasColumnType("varchar(200)")
               .IsRequired();

        builder.Property(r => r.RuleType)
               .HasColumnName("rule_type")
               .HasColumnType("varchar(100)")
               .IsRequired();

        builder.Property(r => r.Description)
               .HasColumnName("description")
               .HasColumnType("text");

        builder.Property(r => r.IsEnabled)
               .HasColumnName("is_enabled")
               .HasDefaultValue(true)
               .IsRequired();

        builder.Property(r => r.Pattern)
               .HasColumnName("pattern")
               .HasColumnType("text");

        builder.Property(r => r.Severity)
               .HasColumnName("severity")
               .HasColumnType("varchar(30)")
               .HasDefaultValue("Warning")
               .IsRequired();

        builder.Property(r => r.ConfigurationJson)
               .HasColumnName("configuration_json")
               .HasColumnType("jsonb")
               .HasDefaultValueSql("'{}'::jsonb")
               .IsRequired();

        builder.Property(r => r.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(r => r.UpdatedAt)
               .HasColumnName("updated_at")
               .HasColumnType("timestamptz");

        builder.HasOne(r => r.Series)
               .WithMany(s => s.ConsistencyRules)
               .HasForeignKey(r => r.SeriesId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
