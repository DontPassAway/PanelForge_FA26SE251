using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Auth;

namespace PanelForge.Infrastructure.Persistence.Configurations.Auth;

internal sealed class WorkspaceAiConfigConfiguration : IEntityTypeConfiguration<WorkspaceAiConfig>
{
    public void Configure(EntityTypeBuilder<WorkspaceAiConfig> builder)
    {
        builder.ToTable("workspace_ai_configs");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(c => c.WorkspaceId)
               .HasColumnName("workspace_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(c => c.WorkspaceId)
               .IsUnique()
               .HasDatabaseName("ix_workspace_ai_configs_workspace_id");

        builder.Property(c => c.Provider)
               .HasColumnName("provider")
               .HasColumnType("varchar(100)")
               .IsRequired();

        builder.Property(c => c.ApiKeyEncrypted)
               .HasColumnName("api_key_encrypted")
               .HasColumnType("text");

        builder.Property(c => c.IsEnabled)
               .HasColumnName("is_enabled")
               .HasColumnType("boolean")
               .HasDefaultValue(true)
               .IsRequired();

        builder.Property(c => c.MonthlyTokenQuota)
               .HasColumnName("monthly_token_quota")
               .HasColumnType("bigint")
               .HasDefaultValue(1000000L)
               .IsRequired();

        builder.Property(c => c.UsedTokensCurrentMonth)
               .HasColumnName("used_tokens_current_month")
               .HasColumnType("bigint")
               .HasDefaultValue(0L)
               .IsRequired();

        builder.Property(c => c.AllowedModelsJson)
               .HasColumnName("allowed_models_json")
               .HasColumnType("text")
               .IsRequired();

        builder.Property(c => c.LastResetAt)
               .HasColumnName("last_reset_at")
               .HasColumnType("timestamptz");

        builder.Property(c => c.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(c => c.UpdatedAt)
               .HasColumnName("updated_at")
               .HasColumnType("timestamptz");

        builder.HasOne(c => c.Workspace)
               .WithOne()
               .HasForeignKey<WorkspaceAiConfig>(c => c.WorkspaceId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
