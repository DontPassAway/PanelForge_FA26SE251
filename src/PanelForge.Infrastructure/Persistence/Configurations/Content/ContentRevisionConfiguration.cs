using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class ContentRevisionConfiguration : IEntityTypeConfiguration<ContentRevision>
{
    public void Configure(EntityTypeBuilder<ContentRevision> builder)
    {
        builder.ToTable("content_revisions");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();

        builder.Property(r => r.ChapterId).HasColumnName("chapter_id").HasColumnType("uuid").IsRequired();
        builder.HasIndex(r => r.ChapterId).HasDatabaseName("ix_content_revisions_chapter_id");
        builder.Property(r => r.SceneId).HasColumnName("scene_id").HasColumnType("uuid").IsRequired();
        builder.Property(r => r.Kind).HasColumnName("kind").HasColumnType("varchar(20)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<ContentRevisionKind>(v)).IsRequired();
        builder.Property(r => r.VersionNumber).HasColumnName("version_number").HasColumnType("int").IsRequired();
        builder.Property(r => r.Summary).HasColumnName("summary").HasColumnType("varchar(1000)").IsRequired();
        builder.Property(r => r.PayloadJson).HasColumnName("payload_json").HasColumnType("text").IsRequired();
        builder.Property(r => r.ContentHash).HasColumnName("content_hash").HasColumnType("varchar(64)").IsRequired();
        builder.Property(r => r.PreviousRevisionId).HasColumnName("previous_revision_id").HasColumnType("uuid");
        builder.Property(r => r.SourceSuggestionId).HasColumnName("source_suggestion_id").HasColumnType("uuid");
        builder.Property(r => r.MappingJson).HasColumnName("mapping_json").HasColumnType("text").IsRequired();

        builder.Property(r => r.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(r => r.CreatedBy).HasColumnName("created_by").HasColumnType("varchar(100)");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
        builder.Property(r => r.UpdatedBy).HasColumnName("updated_by").HasColumnType("varchar(100)");

        // Append-only (BR-01): không soft-delete
        builder.Ignore(r => r.IsDeleted);
        builder.Ignore(r => r.DeletedAt);
        builder.Ignore(r => r.DeletedBy);

        builder.HasIndex(r => new { r.SceneId, r.Kind, r.VersionNumber }).IsUnique().HasDatabaseName("ux_content_revisions_scene_kind_version");

        builder.HasOne<Chapter>().WithMany().HasForeignKey(r => r.ChapterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Scene>().WithMany().HasForeignKey(r => r.SceneId).OnDelete(DeleteBehavior.Restrict);
    }
}