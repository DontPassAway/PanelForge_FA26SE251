using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Auth;

namespace PanelForge.Infrastructure.Persistence.Configurations.Auth;

internal sealed class UserRememberedDeviceConfiguration : IEntityTypeConfiguration<UserRememberedDevice>
{
    public void Configure(EntityTypeBuilder<UserRememberedDevice> builder)
    {
        builder.ToTable("user_remembered_devices");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(d => d.UserId)
               .HasColumnName("user_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(d => d.TokenHash)
               .HasColumnName("token_hash")
               .HasColumnType("varchar(64)")
               .IsRequired();

        builder.HasIndex(d => new { d.UserId, d.TokenHash })
               .HasDatabaseName("ix_user_remembered_devices_user_token");

        builder.Property(d => d.DeviceName)
               .HasColumnName("device_name")
               .HasColumnType("varchar(255)");

        builder.Property(d => d.ExpiresAt)
               .HasColumnName("expires_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(d => d.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(d => d.UpdatedAt)
               .HasColumnName("updated_at")
               .HasColumnType("timestamptz");

        builder.Property(d => d.IsDeleted)
               .HasColumnName("is_deleted")
               .HasDefaultValue(false)
               .IsRequired();

        builder.Property(d => d.DeletedAt)
               .HasColumnName("deleted_at")
               .HasColumnType("timestamptz");

        builder.HasOne(d => d.User)
               .WithMany()
               .HasForeignKey(d => d.UserId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
