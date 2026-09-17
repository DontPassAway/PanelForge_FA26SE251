using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Auth;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Auth;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(u => u.FirebaseUid)
               .HasColumnName("firebase_uid")
               .HasColumnType("varchar(128)");
        builder.HasIndex(u => u.FirebaseUid)
               .IsUnique()
               .HasDatabaseName("ix_users_firebase_uid");

        builder.Property(u => u.Email)
               .HasColumnName("email")
               .HasColumnType("varchar(320)")
               .IsRequired();
        builder.HasIndex(u => u.Email)
               .IsUnique()
               .HasDatabaseName("ix_users_email");

        builder.Property(u => u.PasswordHash)
               .HasColumnName("password_hash")
               .HasColumnType("varchar(255)");

        builder.Property(u => u.FullName)
               .HasColumnName("full_name")
               .HasColumnType("varchar(255)")
               .IsRequired();

        builder.Property(u => u.PhoneNumber)
               .HasColumnName("phone_number")
               .HasColumnType("varchar(20)");

        builder.Property(u => u.AvatarUrl)
               .HasColumnName("avatar_url")
               .HasColumnType("varchar(2048)");

        builder.Property(u => u.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(u => u.IsActive)
               .HasColumnName("is_active")
               .HasDefaultValue(true)
               .IsRequired();

        builder.Property(u => u.Role)
               .HasColumnName("role")
               .HasColumnType("varchar(30)")
               .HasConversion(
                   v => v.ToString(),
                   v => Enum.Parse<SystemRole>(v))
               .HasDefaultValue(SystemRole.User)
               .IsRequired();

        builder.Property(u => u.IsEmailConfirmed)
               .HasColumnName("is_email_confirmed")
               .HasDefaultValue(false)
               .IsRequired();

        builder.Property(u => u.EmailVerificationToken)
               .HasColumnName("email_verification_token")
               .HasColumnType("varchar(255)");

        builder.Property(u => u.EmailVerificationTokenExpiresAt)
               .HasColumnName("email_verification_token_expires_at")
               .HasColumnType("timestamptz");

        builder.Property(u => u.PasswordResetToken)
               .HasColumnName("password_reset_token")
               .HasColumnType("varchar(255)");

        builder.Property(u => u.PasswordResetTokenExpiresAt)
               .HasColumnName("password_reset_token_expires_at")
               .HasColumnType("timestamptz");

        builder.Property(u => u.TwoFactorEnabled)
               .HasColumnName("two_factor_enabled")
               .HasDefaultValue(false)
               .IsRequired();

        builder.Property(u => u.TwoFactorSecret)
               .HasColumnName("two_factor_secret")
               .HasColumnType("varchar(255)");

        builder.HasMany(u => u.OwnedWorkspaces)
               .WithOne(w => w.Owner)
               .HasForeignKey(w => w.OwnerId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(u => u.WorkspaceMemberships)
               .WithOne(m => m.User)
               .HasForeignKey(m => m.UserId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(u => u.CreatedPreviewLinks)
               .WithOne(l => l.CreatedByUser)
               .HasForeignKey(l => l.CreatedByUserId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
