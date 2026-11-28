using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Platform;

namespace PanelForge.Infrastructure.Persistence.Configurations.Platform;

internal sealed class PlatformEmailTemplateConfiguration : IEntityTypeConfiguration<PlatformEmailTemplate>
{
    public void Configure(EntityTypeBuilder<PlatformEmailTemplate> builder)
    {
        builder.ToTable("platform_email_templates");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();

        builder.Property(t => t.TemplateKey).HasColumnName("template_key").HasColumnType("varchar(64)").IsRequired();
        builder.Property(t => t.Subject).HasColumnName("subject")
               .HasColumnType($"varchar({PlatformEmailTemplate.MaxSubjectLength})").IsRequired();
        builder.Property(t => t.Greeting).HasColumnName("greeting").HasColumnType("text").IsRequired();
        builder.Property(t => t.Intro).HasColumnName("intro").HasColumnType("text").IsRequired();
        builder.Property(t => t.Note).HasColumnName("note").HasColumnType("text").IsRequired();
        builder.Property(t => t.Signature).HasColumnName("signature").HasColumnType("text").IsRequired();
        builder.Property(t => t.ChangedByUserId).HasColumnName("changed_by_user_id").HasColumnType("uuid").IsRequired();

        builder.Property(t => t.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(t => t.IsDeleted).HasColumnName("is_deleted").HasColumnType("boolean").HasDefaultValue(false).IsRequired();

        // Mẫu đang dùng của từng loại = bản ghi mới nhất theo template_key
        builder.HasIndex(t => new { t.TemplateKey, t.CreatedAt }).HasDatabaseName("ix_platform_email_templates_key_created_at");

        builder.HasOne<PanelForge.Domain.Entities.Identity.User>()
               .WithMany()
               .HasForeignKey(t => t.ChangedByUserId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
