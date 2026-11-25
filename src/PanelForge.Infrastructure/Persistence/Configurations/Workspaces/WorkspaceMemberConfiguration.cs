using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Workspaces;

internal sealed class WorkspaceMemberConfiguration : IEntityTypeConfiguration<WorkspaceMember>
{
    public void Configure(EntityTypeBuilder<WorkspaceMember> builder)
    {
        builder.ToTable("workspace_members");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(m => m.WorkspaceId)
               .HasColumnName("workspace_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(m => m.UserId)
               .HasColumnName("user_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.HasIndex(m => new { m.WorkspaceId, m.UserId })
               .IsUnique()
               .HasFilter("is_deleted = false")
               .HasDatabaseName("ix_workspace_members_workspace_user");

        builder.Property(m => m.Role)
               .HasColumnName("role")
               .HasColumnType("varchar(30)")
               .HasConversion(
                   v => v.ToString(),
                   v => Enum.Parse<WorkspaceRole>(v))
               .IsRequired();

        builder.Property(m => m.Status)
               .HasColumnName("status")
               .HasColumnType("varchar(30)")
               .HasConversion(
                   v => v.ToString(),
                   v => Enum.Parse<WorkspaceMemberStatus>(v))
               // Không đặt default ở DB: Pending = 0 là giá trị CLR mặc định nên EF sẽ bỏ qua nó và DB ghi Active
               // (lời mời tự được chấp nhận). Giá trị luôn do domain gán (WorkspaceMember.Create mặc định Active).
               .IsRequired();

        builder.Property(m => m.JoinedAt)
               .HasColumnName("joined_at")
               .HasColumnType("timestamptz")
               .IsRequired();
    }
}
