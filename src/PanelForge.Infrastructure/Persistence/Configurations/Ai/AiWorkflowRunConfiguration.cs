using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Ai;

internal sealed class AiWorkflowRunConfiguration : IEntityTypeConfiguration<AiWorkflowRun>
{
    public void Configure(EntityTypeBuilder<AiWorkflowRun> builder)
    {
        builder.ToTable("ai_workflow_runs");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();

        builder.Property(r => r.WorkspaceId).HasColumnName("workspace_id").HasColumnType("uuid").IsRequired();
        builder.Property(r => r.SeriesId).HasColumnName("series_id").HasColumnType("uuid").IsRequired();
        builder.Property(r => r.ChapterId).HasColumnName("chapter_id").HasColumnType("uuid").IsRequired();
        builder.Property(r => r.SceneId).HasColumnName("scene_id").HasColumnType("uuid");
        // Run tạo trước khi có loại workflow đọc như FullScene (giá trị mặc định ở DB cũng là FullScene)
        builder.Property(r => r.WorkflowType).HasColumnName("workflow_type").HasColumnType("varchar(30)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<AiWorkflowType>(v))
               .HasDefaultValue(AiWorkflowType.FullScene).IsRequired();
        builder.Property(r => r.RequestedByUserId).HasColumnName("requested_by_user_id").HasColumnType("uuid").IsRequired();

        builder.Property(r => r.Status).HasColumnName("status").HasColumnType("varchar(30)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<AiRunStatus>(v)).IsRequired();
        builder.Property(r => r.Provider).HasColumnName("provider").HasColumnType("varchar(50)").IsRequired();
        builder.Property(r => r.Model).HasColumnName("model").HasColumnType("varchar(100)").IsRequired();
        builder.Property(r => r.Brief).HasColumnName("brief").HasColumnType("text").IsRequired();
        builder.Property(r => r.ReadingDirection).HasColumnName("reading_direction").HasColumnType("varchar(30)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<ReadingDirection>(v)).IsRequired();
        builder.Property(r => r.MaxRevisionRounds).HasColumnName("max_revision_rounds").HasColumnType("int").IsRequired();
        builder.Property(r => r.RevisionRounds).HasColumnName("revision_rounds").HasColumnType("int").IsRequired();
        builder.Property(r => r.BaseChapterVersion).HasColumnName("base_chapter_version").HasColumnType("bigint").IsRequired();
        builder.Property(r => r.BaseScriptRevisionId).HasColumnName("base_script_revision_id").HasColumnType("uuid");
        // text (không phải jsonb) để giữ nguyên từng byte của snapshot — bất biến, không bị chuẩn hoá lại
        builder.Property(r => r.ContextSnapshotJson).HasColumnName("context_snapshot_json").HasColumnType("text").IsRequired();
        builder.Property(r => r.BibleRevisionIdsJson).HasColumnName("bible_revision_ids_json").HasColumnType("text").IsRequired();
        builder.Property(r => r.ErrorCode).HasColumnName("error_code").HasColumnType("varchar(100)");
        builder.Property(r => r.ErrorMessage).HasColumnName("error_message").HasColumnType("varchar(2000)");
        builder.Property(r => r.LastActivityAtUtc).HasColumnName("last_activity_at_utc").HasColumnType("timestamptz").IsRequired();
        builder.Property(r => r.CompletedAtUtc).HasColumnName("completed_at_utc").HasColumnType("timestamptz");

        builder.Property(r => r.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(r => r.CreatedBy).HasColumnName("created_by").HasColumnType("varchar(100)");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
        builder.Property(r => r.UpdatedBy).HasColumnName("updated_by").HasColumnType("varchar(100)");

        // Provenance không bao giờ xoá mềm/xoá cứng
        builder.Ignore(r => r.IsDeleted);
        builder.Ignore(r => r.DeletedAt);
        builder.Ignore(r => r.DeletedBy);
        builder.Ignore(r => r.IsActive);

        builder.HasIndex(r => new { r.SeriesId, r.CreatedAt }).HasDatabaseName("ix_ai_workflow_runs_series_created");
        // Sweeper tìm run Queued/Running quá hạn
        builder.HasIndex(r => new { r.Status, r.LastActivityAtUtc }).HasDatabaseName("ix_ai_workflow_runs_status_activity");

        builder.HasOne<StudioWorkspace>().WithMany().HasForeignKey(r => r.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Series>().WithMany().HasForeignKey(r => r.SeriesId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Chapter>().WithMany().HasForeignKey(r => r.ChapterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Scene>().WithMany().HasForeignKey(r => r.SceneId).OnDelete(DeleteBehavior.Restrict);
    }
}