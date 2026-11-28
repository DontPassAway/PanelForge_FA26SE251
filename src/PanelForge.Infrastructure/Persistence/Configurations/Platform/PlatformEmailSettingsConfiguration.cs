using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Platform;

namespace PanelForge.Infrastructure.Persistence.Configurations.Platform;

internal sealed class PlatformEmailSettingsConfiguration : IEntityTypeConfiguration<PlatformEmailSettings>
{
    public void Configure(EntityTypeBuilder<PlatformEmailSettings> builder)
    {
        builder.ToTable("platform_email_settings");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();

        builder.Property(s => s.SmtpServer).HasColumnName("smtp_server").HasColumnType("varchar(255)").IsRequired();
        builder.Property(s => s.SmtpPort).HasColumnName("smtp_port").HasColumnType("integer").IsRequired();
        builder.Property(s => s.SenderName).HasColumnName("sender_name").HasColumnType("varchar(100)").IsRequired();
        builder.Property(s => s.SenderEmail).HasColumnName("sender_email").HasColumnType("varchar(254)").IsRequired();
        builder.Property(s => s.ProtectedPassword).HasColumnName("protected_password").HasColumnType("text").IsRequired();
        builder.Property(s => s.ChangedByUserId).HasColumnName("changed_by_user_id").HasColumnType("uuid").IsRequired();

        builder.Property(s => s.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(s => s.IsDeleted).HasColumnName("is_deleted").HasColumnType("boolean").HasDefaultValue(false).IsRequired();

        // Cấu hình đang dùng = bản ghi mới nhất
        builder.HasIndex(s => s.CreatedAt).HasDatabaseName("ix_platform_email_settings_created_at");

        builder.HasOne<PanelForge.Domain.Entities.Identity.User>()
               .WithMany()
               .HasForeignKey(s => s.ChangedByUserId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
