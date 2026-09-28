using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Auth;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Auth;

internal sealed class AiUsageRecordConfiguration : IEntityTypeConfiguration<AiUsageRecord>
{
    public void Configure(EntityTypeBuilder<AiUsageRecord> builder)
    {
        builder.ToTable("ai_usage_records");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(r => r.WorkspaceId)
               .HasColumnName("workspace_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(r => r.UserId)
               .HasColumnName("user_id")
               .HasColumnType("uuid");
        builder.HasIndex(r => r.UserId)
               .HasDatabaseName("ix_ai_usage_records_user_id");

        builder.Property(r => r.Feature)
               .HasColumnName("feature")
               .HasColumnType("varchar(100)")
               .IsRequired();

        builder.Property(r => r.Provider)
               .HasColumnName("provider")
               .HasColumnType("varchar(100)")
               .IsRequired();

        builder.Property(r => r.Model)
               .HasColumnName("model")
               .HasColumnType("varchar(100)");

        builder.Property(r => r.PromptTokens)
               .HasColumnName("prompt_tokens")
               .HasColumnType("bigint")
               .IsRequired();

        builder.Property(r => r.CompletionTokens)
               .HasColumnName("completion_tokens")
               .HasColumnType("bigint")
               .IsRequired();

        builder.Property(r => r.TotalTokens)
               .HasColumnName("total_tokens")
               .HasColumnType("bigint")
               .IsRequired();

        builder.Property(r => r.CostUsd)
               .HasColumnName("cost_usd")
               .HasColumnType("numeric(12,6)");

        builder.Property(r => r.Status)
               .HasColumnName("status")
               .HasColumnType("varchar(30)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<AiUsageStatus>(v))
               .IsRequired();

        builder.Property(r => r.ErrorMessage)
               .HasColumnName("error_message")
               .HasColumnType("varchar(1000)");

        builder.Property(r => r.DurationMs)
               .HasColumnName("duration_ms")
               .HasColumnType("integer");

        builder.Property(r => r.OccurredAtUtc)
               .HasColumnName("occurred_at_utc")
               .HasColumnType("timestamptz")
               .IsRequired();

        // Báo cáo UC-15 lọc theo workspace + khoảng thời gian
        builder.HasIndex(r => new { r.WorkspaceId, r.OccurredAtUtc })
               .HasDatabaseName("ix_ai_usage_records_workspace_occurred");

        builder.Property(r => r.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();
        builder.Property(r => r.CreatedBy).HasColumnName("created_by").HasColumnType("varchar(100)");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
        builder.Property(r => r.UpdatedBy).HasColumnName("updated_by").HasColumnType("varchar(100)");

        // Append-only: không soft-delete
        builder.Ignore(r => r.IsDeleted);
        builder.Ignore(r => r.DeletedAt);
        builder.Ignore(r => r.DeletedBy);

        builder.HasOne(r => r.Workspace)
               .WithMany()
               .HasForeignKey(r => r.WorkspaceId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
