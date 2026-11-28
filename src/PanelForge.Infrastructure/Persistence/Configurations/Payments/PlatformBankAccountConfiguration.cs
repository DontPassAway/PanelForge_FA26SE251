using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Payments;

namespace PanelForge.Infrastructure.Persistence.Configurations.Payments;

internal sealed class PlatformBankAccountConfiguration : IEntityTypeConfiguration<PlatformBankAccount>
{
    public void Configure(EntityTypeBuilder<PlatformBankAccount> builder)
    {
        builder.ToTable("platform_bank_accounts");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();

        builder.Property(a => a.BankBin).HasColumnName("bank_bin").HasColumnType("varchar(6)").IsRequired();
        builder.Property(a => a.BankName).HasColumnName("bank_name").HasColumnType("varchar(100)").IsRequired();
        builder.Property(a => a.AccountNo).HasColumnName("account_no").HasColumnType("varchar(19)").IsRequired();
        builder.Property(a => a.AccountName).HasColumnName("account_name").HasColumnType("varchar(100)").IsRequired();
        builder.Property(a => a.ChangedByUserId).HasColumnName("changed_by_user_id").HasColumnType("uuid").IsRequired();

        builder.Property(a => a.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(a => a.IsDeleted).HasColumnName("is_deleted").HasColumnType("boolean").HasDefaultValue(false).IsRequired();

        // Tài khoản đang dùng = bản ghi mới nhất
        builder.HasIndex(a => a.CreatedAt).HasDatabaseName("ix_platform_bank_accounts_created_at");

        builder.HasOne<PanelForge.Domain.Entities.Identity.User>()
               .WithMany()
               .HasForeignKey(a => a.ChangedByUserId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
