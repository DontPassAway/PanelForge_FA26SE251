using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Workflow;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(n => n.UserId)
               .HasColumnName("user_id")
               .HasColumnType("uuid")
               .IsRequired();
        builder.HasIndex(n => new { n.UserId, n.ReadAt })
               .HasDatabaseName("ix_notifications_user_read");

        builder.Property(n => n.Type)
               .HasColumnName("type")
               .HasColumnType("varchar(40)")
               .HasConversion(
                   v => v.ToString(),
                   v => Enum.Parse<NotificationType>(v))
               .IsRequired();

        builder.Property(n => n.Title)
               .HasColumnName("title")
               .HasColumnType("varchar(200)")
               .IsRequired();

        builder.Property(n => n.Message)
               .HasColumnName("message")
               .HasColumnType("text")
               .IsRequired();

        builder.Property(n => n.AssignmentId)
               .HasColumnName("assignment_id")
               .HasColumnType("uuid");
        builder.HasIndex(n => n.AssignmentId)
               .HasDatabaseName("ix_notifications_assignment_id");

        builder.Property(n => n.ChapterId)
               .HasColumnName("chapter_id")
               .HasColumnType("uuid");

        builder.Property(n => n.ReadAt)
               .HasColumnName("read_at")
               .HasColumnType("timestamptz");

        builder.Property(n => n.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(n => n.UpdatedAt)
               .HasColumnName("updated_at")
               .HasColumnType("timestamptz");

        builder.HasOne<User>()
               .WithMany()
               .HasForeignKey(n => n.UserId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
