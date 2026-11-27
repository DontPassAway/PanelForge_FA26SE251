using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Workspaces;

internal sealed class WorkspaceAutomationSettingsConfiguration : IEntityTypeConfiguration<WorkspaceAutomationSettings>
{
    public void Configure(EntityTypeBuilder<WorkspaceAutomationSettings> builder)
    {
        builder.ToTable("workspace_automation_settings");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(s => s.WorkspaceId).HasColumnName("workspace_id").HasColumnType("uuid").IsRequired();

        // Mặc định đặt trong code (entity), không dùng HasDefaultValue: CLR default làm EF bỏ cột khi INSERT
        builder.Property(s => s.AutoNextStage).HasColumnName("auto_next_stage").HasColumnType("boolean").IsRequired();
        builder.Property(s => s.AssignMode)
               .HasColumnName("assign_mode")
               .HasColumnType("varchar(20)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<AutoAssignMode>(v))
               .IsRequired();
        builder.Property(s => s.AutoRouteReview).HasColumnName("auto_route_review").HasColumnType("boolean").IsRequired();
        builder.Property(s => s.LeadHours).HasColumnName("lead_hours").HasColumnType("integer").IsRequired();
        builder.Property(s => s.EscalationHours).HasColumnName("escalation_hours").HasColumnType("integer");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");

        builder.Property(s => s.RowVersion)
               .HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

        builder.HasIndex(s => s.WorkspaceId).IsUnique().HasFilter("is_deleted = false")
               .HasDatabaseName("ux_workspace_automation_settings_workspace_id");

        builder.HasOne<StudioWorkspace>().WithMany().HasForeignKey(s => s.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}