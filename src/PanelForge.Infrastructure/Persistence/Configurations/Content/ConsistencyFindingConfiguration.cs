using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class ConsistencyFindingConfiguration : IEntityTypeConfiguration<ConsistencyFinding>
{
    public void Configure(EntityTypeBuilder<ConsistencyFinding> builder)
    {
        builder.ToTable("consistency_findings");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();

        builder.Property(f => f.ChapterId).HasColumnName("chapter_id").HasColumnType("uuid").IsRequired();
        builder.Property(f => f.RuleCode).HasColumnName("rule_code").HasColumnType("varchar(60)").IsRequired();
        builder.Property(f => f.ConsistencyRuleId).HasColumnName("consistency_rule_id").HasColumnType("uuid");
        builder.Property(f => f.Severity).HasColumnName("severity").HasColumnType("varchar(20)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<ConsistencyFindingSeverity>(v)).IsRequired();
        builder.Property(f => f.Message).HasColumnName("message").HasColumnType("text").IsRequired();
        builder.Property(f => f.Evidence).HasColumnName("evidence").HasColumnType("text");
        builder.Property(f => f.SceneId).HasColumnName("scene_id").HasColumnType("uuid");
        builder.Property(f => f.ScriptLineId).HasColumnName("script_line_id").HasColumnType("uuid");
        builder.Property(f => f.BibleEntryId).HasColumnName("bible_entry_id").HasColumnType("uuid");
        builder.Property(f => f.Fingerprint).HasColumnName("fingerprint").HasColumnType("varchar(64)").IsRequired();
        builder.Property(f => f.Status).HasColumnName("status").HasColumnType("varchar(30)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<ConsistencyFindingStatus>(v)).IsRequired();
        builder.Property(f => f.TriageNote).HasColumnName("triage_note").HasColumnType("text");
        builder.Property(f => f.TriagedByUserId).HasColumnName("triaged_by_user_id").HasColumnType("uuid");
        builder.Property(f => f.TriagedAt).HasColumnName("triaged_at").HasColumnType("timestamptz");
        builder.Property(f => f.LastDetectedAt).HasColumnName("last_detected_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(f => f.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(f => f.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");

        // Một lỗi (fingerprint) chỉ có một dòng mỗi Chapter; các lần chạy lại cập nhật dòng đó
        builder.HasIndex(f => new { f.ChapterId, f.Fingerprint }).IsUnique()
               .HasFilter("is_deleted = false").HasDatabaseName("ux_consistency_findings_chapter_fingerprint");
        builder.HasIndex(f => new { f.ChapterId, f.Status }).HasDatabaseName("ix_consistency_findings_chapter_status");

        builder.HasOne<Chapter>().WithMany().HasForeignKey(f => f.ChapterId).OnDelete(DeleteBehavior.Cascade);
    }
}
