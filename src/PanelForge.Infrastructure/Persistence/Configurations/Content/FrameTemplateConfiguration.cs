using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class FrameTemplateConfiguration : IEntityTypeConfiguration<FrameTemplate>
{
    public void Configure(EntityTypeBuilder<FrameTemplate> builder)
    {
        builder.ToTable("frame_templates");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();

        builder.Property(t => t.WorkspaceId).HasColumnName("workspace_id").HasColumnType("uuid");
        builder.HasIndex(t => t.WorkspaceId).HasDatabaseName("ix_frame_templates_workspace_id");

        builder.Property(t => t.Name).HasColumnName("name").HasColumnType("varchar(100)").IsRequired();
        builder.Property(t => t.LayoutFormat).HasColumnName("layout_format").HasConversion<string>()
               .HasColumnType("varchar(30)").IsRequired();
        builder.Property(t => t.SlotsJson).HasColumnName("slots_json").HasColumnType("jsonb").IsRequired();

        builder.Property(t => t.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");

        builder.Ignore(t => t.Slots);
        builder.Ignore(t => t.Category);
        builder.Ignore(t => t.Code);
        builder.Ignore(t => t.Description);
    }
}
