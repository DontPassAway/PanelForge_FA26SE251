using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Auth;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Auth;

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
               .HasDatabaseName("ix_workspace_members_workspace_user");

        builder.Property(m => m.Role)
               .HasColumnName("role")
               .HasColumnType("varchar(30)")
               .HasConversion(
                   v => v.ToString(),
                   v => Enum.Parse<WorkspaceRole>(v))
               .IsRequired();

        builder.Property(m => m.JoinedAt)
               .HasColumnName("joined_at")
               .HasColumnType("timestamptz")
               .IsRequired();
    }
}
