using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.MasterData;
using PanelForge.Domain.Entities.Publishing;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Publishing;

internal sealed class ExportJobConfiguration : IEntityTypeConfiguration<ExportJob>
{
    public void Configure(EntityTypeBuilder<ExportJob> builder)
    {
        builder.ToTable("export_jobs");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(j => j.ChapterId).HasColumnName("chapter_id").HasColumnType("uuid").IsRequired();
        builder.HasIndex(j => j.ChapterId).HasDatabaseName("ix_export_jobs_chapter_id");
        builder.Property(j => j.WorkspaceId).HasColumnName("workspace_id").HasColumnType("uuid").IsRequired();
        builder.Property(j => j.ExportPresetId).HasColumnName("export_preset_id").HasColumnType("uuid").IsRequired();
        builder.Property(j => j.Format).HasColumnName("format").HasColumnType("varchar(50)").IsRequired();
        builder.Property(j => j.Status).HasColumnName("status").HasColumnType("varchar(20)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<ExportJobStatus>(v)).IsRequired();
        // Worker lấy job Queued cũ nhất
        builder.HasIndex(j => new { j.Status, j.CreatedAt }).HasDatabaseName("ix_export_jobs_status_created_at");
        builder.Property(j => j.RequestedByUserId).HasColumnName("requested_by_user_id").HasColumnType("uuid").IsRequired();
        builder.Property(j => j.ChapterVersion).HasColumnName("chapter_version").HasColumnType("bigint").IsRequired();
        builder.Property(j => j.SnapshotArtworkIds).HasColumnName("snapshot_artwork_ids").HasColumnType("uuid[]").IsRequired();
        builder.Property(j => j.ResultStorageKey).HasColumnName("result_storage_key").HasColumnType("varchar(500)");
        builder.Property(j => j.ResultFileName).HasColumnName("result_file_name").HasColumnType("varchar(255)");
        builder.Property(j => j.ResultContentType).HasColumnName("result_content_type").HasColumnType("varchar(100)");
        builder.Property(j => j.ResultSizeBytes).HasColumnName("result_size_bytes").HasColumnType("bigint");
        builder.Property(j => j.ResultSha256).HasColumnName("result_sha256").HasColumnType("varchar(64)");
        builder.Property(j => j.Error).HasColumnName("error").HasColumnType("text");
        builder.Property(j => j.StartedAt).HasColumnName("started_at").HasColumnType("timestamptz");
        builder.Property(j => j.CompletedAt).HasColumnName("completed_at").HasColumnType("timestamptz");
        builder.Property(j => j.DownloadCount).HasColumnName("download_count").HasColumnType("integer").IsRequired();
        builder.Property(j => j.LastDownloadedAt).HasColumnName("last_downloaded_at").HasColumnType("timestamptz");
        builder.Property(j => j.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(j => j.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
        builder.HasOne<Chapter>().WithMany().HasForeignKey(j => j.ChapterId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ExportPreset>().WithMany().HasForeignKey(j => j.ExportPresetId).OnDelete(DeleteBehavior.Restrict);
    }
}
