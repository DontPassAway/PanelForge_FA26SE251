using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Payments;

namespace PanelForge.Infrastructure.Persistence.Configurations.Payments;

internal sealed class ChapterEntitlementConfiguration : IEntityTypeConfiguration<ChapterEntitlement>
{
    public void Configure(EntityTypeBuilder<ChapterEntitlement> builder)
    {
        builder.ToTable("chapter_entitlements");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(e => e.UserId)
               .HasColumnName("user_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(e => e.ChapterId)
               .HasColumnName("chapter_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(e => e.SeriesId)
               .HasColumnName("series_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(e => e.WorkspaceId)
               .HasColumnName("workspace_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(e => e.TransactionId)
               .HasColumnName("transaction_id")
               .HasColumnType("uuid");

        builder.Property(e => e.PricePaidVnd)
               .HasColumnName("price_paid_vnd")
               .HasColumnType("numeric(15,2)")
               .HasDefaultValue(0m)
               .IsRequired();

        builder.Property(e => e.UnlockedAt)
               .HasColumnName("unlocked_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(e => e.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(e => e.UpdatedAt)
               .HasColumnName("updated_at")
               .HasColumnType("timestamptz");

        builder.Property(e => e.IsDeleted)
               .HasColumnName("is_deleted")
               .HasColumnType("boolean")
               .HasDefaultValue(false)
               .IsRequired();

        // 1 user only unlocks a specific chapter once
        builder.HasIndex(e => new { e.UserId, e.ChapterId })
               .IsUnique()
               .HasFilter("is_deleted = false")
               .HasDatabaseName("ix_chapter_entitlements_user_chapter");

        builder.HasIndex(e => e.WorkspaceId)
               .HasDatabaseName("ix_chapter_entitlements_workspace_id");

        builder.HasOne(e => e.User)
               .WithMany()
               .HasForeignKey(e => e.UserId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Chapter)
               .WithMany()
               .HasForeignKey(e => e.ChapterId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Series)
               .WithMany()
               .HasForeignKey(e => e.SeriesId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Workspace)
               .WithMany()
               .HasForeignKey(e => e.WorkspaceId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Transaction)
               .WithMany()
               .HasForeignKey(e => e.TransactionId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}

