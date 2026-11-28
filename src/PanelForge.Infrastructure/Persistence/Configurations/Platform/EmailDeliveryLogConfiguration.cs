using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Platform;

namespace PanelForge.Infrastructure.Persistence.Configurations.Platform;

internal sealed class EmailDeliveryLogConfiguration : IEntityTypeConfiguration<EmailDeliveryLog>
{
    public void Configure(EntityTypeBuilder<EmailDeliveryLog> builder)
    {
        builder.ToTable("email_delivery_logs");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();

        builder.Property(l => l.ToEmail).HasColumnName("to_email").HasColumnType("varchar(254)").IsRequired();
        builder.Property(l => l.MailType).HasColumnName("mail_type").HasColumnType("varchar(40)").IsRequired();
        builder.Property(l => l.Subject).HasColumnName("subject").HasColumnType("varchar(300)").IsRequired();
        builder.Property(l => l.Succeeded).HasColumnName("succeeded").HasColumnType("boolean").IsRequired();
        builder.Property(l => l.FailureReason).HasColumnName("failure_reason").HasColumnType("varchar(40)");
        builder.Property(l => l.ErrorDetail).HasColumnName("error_detail").HasColumnType("varchar(1000)");
        builder.Property(l => l.SmtpServer).HasColumnName("smtp_server").HasColumnType("varchar(255)");
        builder.Property(l => l.SmtpPort).HasColumnName("smtp_port").HasColumnType("integer");

        builder.Property(l => l.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(l => l.IsDeleted).HasColumnName("is_deleted").HasColumnType("boolean").HasDefaultValue(false).IsRequired();

        // Admin xem mới nhất trước; System Health đếm lỗi 24h
        builder.HasIndex(l => l.CreatedAt).HasDatabaseName("ix_email_delivery_logs_created_at");
        builder.HasIndex(l => new { l.Succeeded, l.CreatedAt }).HasDatabaseName("ix_email_delivery_logs_succeeded_created_at");
    }
}
