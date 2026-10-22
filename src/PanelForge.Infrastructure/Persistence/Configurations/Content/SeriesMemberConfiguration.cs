using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class SeriesMemberConfiguration : IEntityTypeConfiguration<SeriesMember>
{
    public void Configure(EntityTypeBuilder<SeriesMember> builder)
    {
        builder.ToTable("series_members");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
               .HasColumnName("id")
               .HasColumnType("uuid")
               .ValueGeneratedNever();

        builder.Property(m => m.SeriesId)
               .HasColumnName("series_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(m => m.UserId)
               .HasColumnName("user_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.HasIndex(m => new { m.SeriesId, m.UserId })
               .IsUnique()
               .HasDatabaseName("ix_series_members_series_user");

        builder.Property(m => m.Role)
               .HasColumnName("role")
               .HasColumnType("varchar(30)")
               .HasConversion(
                   v => v.ToString(),
                   v => Enum.Parse<WorkspaceRole>(v))
               .IsRequired();

        builder.Property(m => m.AssignedAt)
               .HasColumnName("assigned_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.HasOne(m => m.Series)
               .WithMany(s => s.Members)
               .HasForeignKey(m => m.SeriesId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.User)
               .WithMany()
               .HasForeignKey(m => m.UserId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
