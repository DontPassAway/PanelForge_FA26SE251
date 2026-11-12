using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Payments;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Payments;

internal sealed class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> builder)
    {
        builder.ToTable("payment_transactions");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(t => t.UserId)
               .HasColumnName("user_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(t => t.WorkspaceId)
               .HasColumnName("workspace_id")
               .HasColumnType("uuid");

        builder.Property(t => t.SeriesId)
               .HasColumnName("series_id")
               .HasColumnType("uuid");

        builder.Property(t => t.ChapterId)
               .HasColumnName("chapter_id")
               .HasColumnType("uuid");

        builder.Property(t => t.OrderCode)
               .HasColumnName("order_code")
               .HasColumnType("varchar(64)")
               .IsRequired();

        builder.HasIndex(t => t.OrderCode)
               .IsUnique()
               .HasDatabaseName("ix_payment_transactions_order_code");

        builder.Property(t => t.Type)
               .HasColumnName("type")
               .HasColumnType("varchar(30)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<PaymentTransactionType>(v))
               .IsRequired();

        builder.Property(t => t.AmountVnd)
               .HasColumnName("amount_vnd")
               .HasColumnType("numeric(15,2)")
               .IsRequired();

        builder.Property(t => t.Status)
               .HasColumnName("status")
               .HasColumnType("varchar(30)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<PaymentTransactionStatus>(v))
               .HasDefaultValue(PaymentTransactionStatus.Pending)
               .HasSentinel(PaymentTransactionStatus.Pending)
               .IsRequired();

        builder.Property(t => t.QrImageUrl)
               .HasColumnName("qr_image_url")
               .HasColumnType("varchar(1000)")
               .IsRequired();

        builder.Property(t => t.BankBin)
               .HasColumnName("bank_bin")
               .HasColumnType("varchar(20)")
               .IsRequired();

        builder.Property(t => t.BankAccountNo)
               .HasColumnName("bank_account_no")
               .HasColumnType("varchar(50)")
               .IsRequired();

        builder.Property(t => t.BankAccountName)
               .HasColumnName("bank_account_name")
               .HasColumnType("varchar(200)")
               .IsRequired();

        builder.Property(t => t.ConfirmedAt)
               .HasColumnName("confirmed_at")
               .HasColumnType("timestamptz");

        builder.Property(t => t.ConfirmedByUserId)
               .HasColumnName("confirmed_by_user_id")
               .HasColumnType("uuid");

        builder.Property(t => t.RejectionReason)
               .HasColumnName("rejection_reason")
               .HasColumnType("text");

        builder.Property(t => t.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(t => t.UpdatedAt)
               .HasColumnName("updated_at")
               .HasColumnType("timestamptz");

        builder.Property(t => t.IsDeleted)
               .HasColumnName("is_deleted")
               .HasColumnType("boolean")
               .HasDefaultValue(false)
               .IsRequired();

        // Indexes for Producer and User query performance
        builder.HasIndex(t => new { t.WorkspaceId, t.Status })
               .HasDatabaseName("ix_payment_transactions_workspace_status");

        builder.HasIndex(t => new { t.UserId, t.Status })
               .HasDatabaseName("ix_payment_transactions_user_status");

        // Relationships
        builder.HasOne(t => t.User)
               .WithMany()
               .HasForeignKey(t => t.UserId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Workspace)
               .WithMany()
               .HasForeignKey(t => t.WorkspaceId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.Series)
               .WithMany()
               .HasForeignKey(t => t.SeriesId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.Chapter)
               .WithMany()
               .HasForeignKey(t => t.ChapterId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.ConfirmedByUser)
               .WithMany()
               .HasForeignKey(t => t.ConfirmedByUserId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}

