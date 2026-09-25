using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.MasterData;

namespace PanelForge.Infrastructure.Persistence.Configurations.MasterData;

internal sealed class PipelineTemplateConfiguration : IEntityTypeConfiguration<PipelineTemplate>
{
    public void Configure(EntityTypeBuilder<PipelineTemplate> builder)
    {
        builder.ToTable("pipeline_templates");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(t => t.Code)
               .HasColumnName("code")
               .HasColumnType("varchar(100)")
               .IsRequired();
        builder.HasIndex(t => t.Code)
               .IsUnique()
               .HasDatabaseName("ix_pipeline_templates_code");

        builder.Property(t => t.Name)
               .HasColumnName("name")
               .HasColumnType("varchar(200)")
               .IsRequired();

        builder.Property(t => t.Description)
               .HasColumnName("description")
               .HasColumnType("text");

        builder.Property(t => t.IsDefault)
               .HasColumnName("is_default")
               .HasDefaultValue(false)
               .IsRequired();

        builder.Property(t => t.IsActive)
               .HasColumnName("is_active")
               .HasDefaultValue(true)
               .IsRequired();

        builder.Property(t => t.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(t => t.UpdatedAt)
               .HasColumnName("updated_at")
               .HasColumnType("timestamptz");

        builder.HasMany(t => t.Stages)
               .WithOne(s => s.Template)
               .HasForeignKey(s => s.PipelineTemplateId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(t => t.IsDeleted);
        builder.Ignore(t => t.DeletedAt);
        builder.Ignore(t => t.DeletedBy);
        builder.Ignore(t => t.CreatedBy);
        builder.Ignore(t => t.UpdatedBy);
    }
}
