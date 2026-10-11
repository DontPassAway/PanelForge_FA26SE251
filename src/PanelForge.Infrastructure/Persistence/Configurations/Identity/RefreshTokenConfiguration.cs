using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Identity;

namespace PanelForge.Infrastructure.Persistence.Configurations.Identity;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(t => t.UserId).HasColumnName("user_id").HasColumnType("uuid").IsRequired();
        builder.Property(t => t.TokenHash).HasColumnName("token_hash").HasColumnType("varchar(64)").IsRequired();
        builder.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("ix_refresh_tokens_token_hash");
        builder.Property(t => t.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(t => t.RevokedAt).HasColumnName("revoked_at").HasColumnType("timestamptz");
        builder.Property(t => t.SessionId).HasColumnName("session_id").HasColumnType("uuid").IsRequired();
        builder.Property(t => t.SessionStartedAt).HasColumnName("session_started_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(t => t.DeviceName).HasColumnName("device_name").HasMaxLength(150);
        builder.Property(t => t.IpAddress).HasColumnName("ip_address").HasMaxLength(64);
        builder.Property(t => t.UserAgent).HasColumnName("user_agent").HasMaxLength(512);
        builder.Property(t => t.LastActiveAt).HasColumnName("last_active_at").HasColumnType("timestamptz").IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
        builder.Property(t => t.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false).IsRequired();
        builder.Property(t => t.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamptz");

        builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
