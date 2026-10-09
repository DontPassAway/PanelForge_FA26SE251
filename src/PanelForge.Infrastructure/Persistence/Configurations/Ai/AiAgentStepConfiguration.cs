using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Ai;

internal sealed class AiAgentStepConfiguration : IEntityTypeConfiguration<AiAgentStep>
{
    public void Configure(EntityTypeBuilder<AiAgentStep> builder)
    {
        builder.ToTable("ai_agent_steps");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();

        builder.Property(s => s.RunId).HasColumnName("run_id").HasColumnType("uuid").IsRequired();
        builder.Property(s => s.StepIndex).HasColumnName("step_index").HasColumnType("int").IsRequired();
        builder.Property(s => s.Agent).HasColumnName("agent").HasColumnType("varchar(30)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<AiAgentType>(v)).IsRequired();
        builder.Property(s => s.Round).HasColumnName("round").HasColumnType("int").IsRequired();
        builder.Property(s => s.ParentStepId).HasColumnName("parent_step_id").HasColumnType("uuid");
        builder.Property(s => s.Status).HasColumnName("status").HasColumnType("varchar(20)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<AiStepStatus>(v)).IsRequired();
        builder.Property(s => s.Provider).HasColumnName("provider").HasColumnType("varchar(50)").IsRequired();
        builder.Property(s => s.Model).HasColumnName("model").HasColumnType("varchar(100)").IsRequired();
        builder.Property(s => s.PromptVersion).HasColumnName("prompt_version").HasColumnType("varchar(50)").IsRequired();
        builder.Property(s => s.PromptRef).HasColumnName("prompt_ref").HasColumnType("varchar(300)").IsRequired();
        builder.Property(s => s.ParametersJson).HasColumnName("parameters_json").HasColumnType("text").IsRequired();
        builder.Property(s => s.InputJson).HasColumnName("input_json").HasColumnType("text").IsRequired();
        builder.Property(s => s.OutputJson).HasColumnName("output_json").HasColumnType("text");
        builder.Property(s => s.StartedAtUtc).HasColumnName("started_at_utc").HasColumnType("timestamptz").IsRequired();
        builder.Property(s => s.CompletedAtUtc).HasColumnName("completed_at_utc").HasColumnType("timestamptz").IsRequired();
        builder.Property(s => s.PromptTokens).HasColumnName("prompt_tokens").HasColumnType("bigint");
        builder.Property(s => s.CompletionTokens).HasColumnName("completion_tokens").HasColumnType("bigint");
        builder.Property(s => s.RepairAttempts).HasColumnName("repair_attempts").HasColumnType("int").IsRequired();
        builder.Property(s => s.UsageRecordId).HasColumnName("usage_record_id").HasColumnType("uuid");
        builder.Property(s => s.ErrorCode).HasColumnName("error_code").HasColumnType("varchar(100)");
        builder.Property(s => s.ErrorMessage).HasColumnName("error_message").HasColumnType("varchar(2000)");

        builder.Property(s => s.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("created_by").HasColumnType("varchar(100)");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by").HasColumnType("varchar(100)");

        builder.Ignore(s => s.IsDeleted);
        builder.Ignore(s => s.DeletedAt);
        builder.Ignore(s => s.DeletedBy);
        builder.Ignore(s => s.DurationMs);

        // Chống lưu trùng step khi AI service retry callback
        builder.HasIndex(s => new { s.RunId, s.StepIndex }).IsUnique().HasDatabaseName("ux_ai_agent_steps_run_step");

        builder.HasOne<AiWorkflowRun>().WithMany().HasForeignKey(s => s.RunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AiUsageRecord>().WithMany().HasForeignKey(s => s.UsageRecordId).OnDelete(DeleteBehavior.Restrict);
    }
}