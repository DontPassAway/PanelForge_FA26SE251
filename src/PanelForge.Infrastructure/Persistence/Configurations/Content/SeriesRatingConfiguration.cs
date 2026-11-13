using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Identity;

namespace PanelForge.Infrastructure.Persistence.Configurations.Content;

internal sealed class SeriesRatingConfiguration : IEntityTypeConfiguration<SeriesRating>
{
    public void Configure(EntityTypeBuilder<SeriesRating> builder)
    {
        builder.ToTable("series_ratings", t => t.HasCheckConstraint("ck_series_ratings_stars", "stars BETWEEN 1 AND 5"));

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(r => r.SeriesId).HasColumnName("series_id").HasColumnType("uuid").IsRequired();
        builder.Property(r => r.UserId).HasColumnName("user_id").HasColumnType("uuid").IsRequired();
        builder.Property(r => r.Stars).HasColumnName("stars").HasColumnType("int").IsRequired();
        builder.Property(r => r.Language).HasColumnName("language").HasColumnType("varchar(8)").HasDefaultValue("vi").IsRequired();
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
        builder.Property(r => r.IsDeleted).HasColumnName("is_deleted").HasColumnType("boolean").HasDefaultValue(false).IsRequired();

        // Mỗi độc giả một đánh giá cho một bộ truyện.
        builder.HasIndex(r => new { r.SeriesId, r.UserId })
               .IsUnique()
               .HasFilter("is_deleted = false")
               .HasDatabaseName("ix_series_ratings_series_user");

        builder.HasOne<Series>().WithMany().HasForeignKey(r => r.SeriesId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
