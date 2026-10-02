using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Ai;

internal sealed class AiSuggestionConfiguration : IEntityTypeConfiguration<AiSuggestion>
{
    public void Configure(EntityTypeBuilder<AiSuggestion> builder)
    {
        builder.ToTable("ai_suggestions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();

        builder.Property(s => s.RunId).HasColumnName("run_id").HasColumnType("uuid").IsRequired();
        builder.Property(s => s.StepId).HasColumnName("step_id").HasColumnType("uuid").IsRequired();
        builder.Property(s => s.WorkspaceId).HasColumnName("workspace_id").HasColumnType("uuid").IsRequired();
        builder.Property(s => s.SeriesId).HasColumnName("series_id").HasColumnType("uuid").IsRequired();
        builder.Property(s => s.ChapterId).HasColumnName("chapter_id").HasColumnType("uuid").IsRequired();
        builder.Property(s => s.SceneId).HasColumnName("scene_id").HasColumnType("uuid").IsRequired();
        builder.Property(s => s.Kind).HasColumnName("kind").HasColumnType("varchar(20)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<AiSuggestionKind>(v)).IsRequired();
        builder.Property(s => s.Status).HasColumnName("status").HasColumnType("varchar(30)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<AiSuggestionStatus>(v)).IsRequired();
        builder.Property(s => s.PayloadJson).HasColumnName("payload_json").HasColumnType("text").IsRequired();
        builder.Property(s => s.PayloadHash).HasColumnName("payload_hash").HasColumnType("varchar(64)").IsRequired();
        builder.Property(s => s.Summary).HasColumnName("summary").HasColumnType("varchar(1000)").IsRequired();
        builder.Property(s => s.BaseChapterVersion).HasColumnName("base_chapter_version").HasColumnType("bigint").IsRequired();
        builder.Property(s => s.BaseScriptRevisionId).HasColumnName("base_script_revision_id").HasColumnType("uuid");
        builder.Property(s => s.BibleRevisionIdsJson).HasColumnName("bible_revision_ids_json").HasColumnType("text").IsRequired();
        builder.Property(s => s.ParentSuggestionId).HasColumnName("parent_suggestion_id").HasColumnType("uuid");
        builder.Property(s => s.HasBlockingFindings).HasColumnName("has_blocking_findings").HasColumnType("boolean").IsRequired();

        builder.Property(s => s.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("created_by").HasColumnType("varchar(100)");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by").HasColumnType("varchar(100)");

        builder.Ignore(s => s.IsDeleted);
        builder.Ignore(s => s.DeletedAt);
        builder.Ignore(s => s.DeletedBy);

        builder.HasIndex(s => new { s.SeriesId, s.Status }).HasDatabaseName("ix_ai_suggestions_series_status");
        builder.HasIndex(s => s.RunId).HasDatabaseName("ix_ai_suggestions_run");

        builder.HasOne<AiWorkflowRun>().WithMany().HasForeignKey(s => s.RunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AiAgentStep>().WithMany().HasForeignKey(s => s.StepId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AiSuggestion>().WithMany().HasForeignKey(s => s.ParentSuggestionId).OnDelete(DeleteBehavior.Restrict);
    }
}