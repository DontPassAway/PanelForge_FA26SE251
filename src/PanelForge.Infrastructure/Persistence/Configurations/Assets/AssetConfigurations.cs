using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Assets;

namespace PanelForge.Infrastructure.Persistence.Configurations.Assets;

internal sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.ToTable("assets");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();

        builder.Property(a => a.WorkspaceId).HasColumnName("workspace_id").HasColumnType("uuid").IsRequired();
        builder.Property(a => a.Sha256).HasColumnName("sha256").HasColumnType("varchar(64)").IsRequired();
        // BR-13: một nội dung chỉ có một Asset trong mỗi Workspace
        builder.HasIndex(a => new { a.WorkspaceId, a.Sha256 }).IsUnique().HasDatabaseName("ix_assets_workspace_sha256");

        builder.Property(a => a.SizeBytes).HasColumnName("size_bytes").HasColumnType("bigint").IsRequired();
        builder.Property(a => a.ContentType).HasColumnName("content_type").HasColumnType("varchar(100)").IsRequired();
        builder.Property(a => a.StorageKey).HasColumnName("storage_key").HasColumnType("varchar(200)").IsRequired();

        builder.Property(a => a.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
    }
}

internal sealed class AssetReferenceConfiguration : IEntityTypeConfiguration<AssetReference>
{
    public void Configure(EntityTypeBuilder<AssetReference> builder)
    {
        builder.ToTable("asset_references");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();

        builder.Property(r => r.AssetId).HasColumnName("asset_id").HasColumnType("uuid").IsRequired();
        builder.HasIndex(r => r.AssetId).HasDatabaseName("ix_asset_references_asset_id");

        builder.Property(r => r.WorkspaceId).HasColumnName("workspace_id").HasColumnType("uuid").IsRequired();
        builder.Property(r => r.ChapterId).HasColumnName("chapter_id").HasColumnType("uuid").IsRequired();
        builder.HasIndex(r => r.ChapterId).HasDatabaseName("ix_asset_references_chapter_id");

        builder.Property(r => r.PageId).HasColumnName("page_id").HasColumnType("uuid").IsRequired();
        builder.Property(r => r.PipelineStageId).HasColumnName("pipeline_stage_id").HasColumnType("uuid").IsRequired();
        builder.Property(r => r.VersionNumber).HasColumnName("version_number").HasColumnType("int").IsRequired();
        // Phiên bản tăng dần theo (Page, Stage), không trùng (BR-01)
        builder.HasIndex(r => new { r.PageId, r.PipelineStageId, r.VersionNumber })
               .IsUnique().HasDatabaseName("ix_asset_references_page_stage_version");

        builder.Property(r => r.LayerType).HasColumnName("layer_type").HasColumnType("varchar(40)");
        builder.Property(r => r.FileName).HasColumnName("file_name").HasColumnType("varchar(255)").IsRequired();
        builder.Property(r => r.UploadedByUserId).HasColumnName("uploaded_by_user_id").HasColumnType("uuid").IsRequired();

        builder.Property(r => r.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");

        builder.HasOne<Asset>().WithMany().HasForeignKey(r => r.AssetId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AssetAccessLogConfiguration : IEntityTypeConfiguration<AssetAccessLog>
{
    public void Configure(EntityTypeBuilder<AssetAccessLog> builder)
    {
        builder.ToTable("asset_access_logs");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();

        builder.Property(l => l.AssetReferenceId).HasColumnName("asset_reference_id").HasColumnType("uuid").IsRequired();
        builder.HasIndex(l => l.AssetReferenceId).HasDatabaseName("ix_asset_access_logs_reference_id");

        builder.Property(l => l.UserId).HasColumnName("user_id").HasColumnType("uuid").IsRequired();
        builder.Property(l => l.IpAddress).HasColumnName("ip_address").HasColumnType("varchar(64)");
        builder.Property(l => l.AccessedAt).HasColumnName("accessed_at").HasColumnType("timestamptz").IsRequired();

        builder.Property(l => l.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");

        builder.HasOne<AssetReference>().WithMany().HasForeignKey(l => l.AssetReferenceId).OnDelete(DeleteBehavior.Cascade);
    }
}
