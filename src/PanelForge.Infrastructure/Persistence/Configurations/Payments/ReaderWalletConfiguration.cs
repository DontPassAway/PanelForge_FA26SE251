using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Payments;

namespace PanelForge.Infrastructure.Persistence.Configurations.Payments;

internal sealed class ReaderWalletConfiguration : IEntityTypeConfiguration<ReaderWallet>
{
    public void Configure(EntityTypeBuilder<ReaderWallet> builder)
    {
        builder.ToTable("reader_wallets");

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(w => w.UserId)
               .HasColumnName("user_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.HasIndex(w => w.UserId)
               .IsUnique()
               .HasDatabaseName("ix_reader_wallets_user_id");

        builder.Property(w => w.BalanceVnd)
               .HasColumnName("balance_vnd")
               .HasColumnType("numeric(15,2)")
               .HasDefaultValue(0m)
               .IsRequired();

        builder.Property(w => w.IsVip)
               .HasColumnName("is_vip")
               .HasColumnType("boolean")
               .HasDefaultValue(false)
               .IsRequired();

        builder.Property(w => w.VipExpiresAt)
               .HasColumnName("vip_expires_at")
               .HasColumnType("timestamptz");

        builder.Property(w => w.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(w => w.UpdatedAt)
               .HasColumnName("updated_at")
               .HasColumnType("timestamptz");

        builder.Property(w => w.IsDeleted)
               .HasColumnName("is_deleted")
               .HasColumnType("boolean")
               .HasDefaultValue(false)
               .IsRequired();

        builder.HasOne(w => w.User)
               .WithOne()
               .HasForeignKey<ReaderWallet>(w => w.UserId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

