using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Auth;

namespace PanelForge.Infrastructure.Persistence.Configurations.Auth;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(a => a.WorkspaceId)
               .HasColumnName("workspace_id")
               .HasColumnType("uuid");
        builder.HasIndex(a => a.WorkspaceId)
               .HasDatabaseName("ix_audit_logs_workspace_id");

        builder.Property(a => a.UserId)
               .HasColumnName("user_id")
               .HasColumnType("uuid");
        builder.HasIndex(a => a.UserId)
               .HasDatabaseName("ix_audit_logs_user_id");

        builder.Property(a => a.UserEmail)
               .HasColumnName("user_email")
               .HasColumnType("varchar(255)");

        builder.Property(a => a.Action)
               .HasColumnName("action")
               .HasColumnType("varchar(100)")
               .IsRequired();
        builder.HasIndex(a => a.Action)
               .HasDatabaseName("ix_audit_logs_action");

        builder.Property(a => a.EntityName)
               .HasColumnName("entity_name")
               .HasColumnType("varchar(100)")
               .IsRequired();

        builder.Property(a => a.EntityId)
               .HasColumnName("entity_id")
               .HasColumnType("varchar(100)");

        builder.Property(a => a.ChangesJson)
               .HasColumnName("changes_json")
               .HasColumnType("text");

        builder.Property(a => a.TimestampUtc)
               .HasColumnName("timestamp_utc")
               .HasColumnType("timestamptz")
               .IsRequired();
        builder.HasIndex(a => a.TimestampUtc)
               .HasDatabaseName("ix_audit_logs_timestamp_utc");

        builder.Property(a => a.IpAddress)
               .HasColumnName("ip_address")
               .HasColumnType("varchar(45)");

        builder.Property(a => a.Details)
               .HasColumnName("details")
               .HasColumnType("text");

        // UC-15: lọc theo entity (và tra lịch sử một bản ghi cụ thể)
        builder.HasIndex(a => new { a.EntityName, a.EntityId })
               .HasDatabaseName("ix_audit_logs_entity_name_entity_id");

        builder.Property(a => a.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(a => a.UpdatedAt)
               .HasColumnName("updated_at")
               .HasColumnType("timestamptz");
    }
}
