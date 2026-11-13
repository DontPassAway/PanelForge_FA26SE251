using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class PanelConfiguration : IEntityTypeConfiguration<Panel>
{
    public void Configure(EntityTypeBuilder<Panel> builder)
    {
        builder.ToTable("panels");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(p => p.PageId)
               .HasColumnName("page_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(p => p.PanelNumber)
               .HasColumnName("panel_number")
               .HasColumnType("int")
               .IsRequired();

        builder.HasIndex(p => new { p.PageId, p.PanelNumber })
               .IsUnique()
               .HasFilter("is_deleted = false") // trang/khung/cảnh đã xoá mềm không giữ chỗ số thứ tự
               .HasDatabaseName("ix_panels_page_number");

        builder.Property(p => p.BoundingBox)
               .HasColumnName("bounding_box")
               .HasColumnType("jsonb")
               .HasDefaultValueSql("'{}'::jsonb")
               .IsRequired();

        builder.Property(p => p.ReadingOrder)
               .HasColumnName("reading_order")
               .HasColumnType("int")
               .IsRequired();

        // BR-11: chống ghi đè khi hai người sửa cùng panel
        builder.Property(p => p.Version)
               .HasColumnName("version")
               .HasColumnType("int")
               .HasDefaultValue(0)
               .IsConcurrencyToken()
               .IsRequired();

        builder.Property(p => p.Direction)
               .HasColumnName("direction")
               .HasColumnType("varchar(1000)");

        builder.Property(p => p.CurrentStageId)
               .HasColumnName("current_stage_id")
               .HasColumnType("uuid");

        builder.Property(p => p.SceneId)
               .HasColumnName("scene_id")
               .HasColumnType("uuid");

        builder.HasIndex(p => p.SceneId)
               .HasDatabaseName("ix_panels_scene");

        builder.HasOne(p => p.CurrentStage)
               .WithMany()
               .HasForeignKey(p => p.CurrentStageId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}
