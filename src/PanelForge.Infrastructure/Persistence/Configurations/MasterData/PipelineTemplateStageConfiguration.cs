using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.MasterData;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.MasterData;

internal sealed class PipelineTemplateStageConfiguration : IEntityTypeConfiguration<PipelineTemplateStage>
{
    public void Configure(EntityTypeBuilder<PipelineTemplateStage> builder)
    {
        builder.ToTable("pipeline_template_stages");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(s => s.PipelineTemplateId)
               .HasColumnName("pipeline_template_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(s => s.Code)
               .HasColumnName("code")
               .HasColumnType("varchar(100)")
               .IsRequired();

        builder.Property(s => s.Name)
               .HasColumnName("name")
               .HasColumnType("varchar(200)")
               .IsRequired();

        builder.Property(s => s.Order)
               .HasColumnName("stage_order")
               .IsRequired();

        builder.Property(s => s.RequiredRole)
               .HasColumnName("required_role")
               .HasColumnType("varchar(30)")
               .HasConversion(
                   v => v.HasValue ? v.Value.ToString() : null,
                   v => v == null ? (WorkspaceRole?)null : Enum.Parse<WorkspaceRole>(v));

        builder.Property(s => s.GateType)
               .HasColumnName("gate_type")
               .HasColumnType("varchar(50)");

        builder.Property(s => s.IsRequired)
               .HasColumnName("is_required")
               .HasDefaultValue(true)
               .IsRequired();

        builder.Property(s => s.ConfigurationJson)
               .HasColumnName("configuration_json")
               .HasColumnType("jsonb")
               .HasDefaultValueSql("'{}'::jsonb")
               .IsRequired();

        builder.Property(s => s.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(s => s.UpdatedAt)
               .HasColumnName("updated_at")
               .HasColumnType("timestamptz");

        // UNIQUE(PipelineTemplateId, Order) — stage order must be unique within a template
        builder.HasIndex(s => new { s.PipelineTemplateId, s.Order })
               .IsUnique()
               .HasDatabaseName("ix_pipeline_template_stages_template_order");

        builder.Ignore(s => s.IsDeleted);
        builder.Ignore(s => s.DeletedAt);
        builder.Ignore(s => s.DeletedBy);
        builder.Ignore(s => s.CreatedBy);
        builder.Ignore(s => s.UpdatedBy);
    }
}
