using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Configurations.Ai;

internal sealed class AiSuggestionDecisionConfiguration : IEntityTypeConfiguration<AiSuggestionDecision>
{
    public void Configure(EntityTypeBuilder<AiSuggestionDecision> builder)
    {
        builder.ToTable("ai_suggestion_decisions");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();

        builder.Property(d => d.SuggestionId).HasColumnName("suggestion_id").HasColumnType("uuid").IsRequired();
        builder.Property(d => d.DecidedByUserId).HasColumnName("decided_by_user_id").HasColumnType("uuid").IsRequired();
        builder.Property(d => d.Decision).HasColumnName("decision").HasColumnType("varchar(30)")
               .HasConversion(v => v.ToString(), v => Enum.Parse<AiSuggestionStatus>(v)).IsRequired();
        builder.Property(d => d.Reason).HasColumnName("reason").HasColumnType("varchar(2000)");
        builder.Property(d => d.DecidedAtUtc).HasColumnName("decided_at_utc").HasColumnType("timestamptz").IsRequired();
        builder.Property(d => d.ChapterVersionAtDecision).HasColumnName("chapter_version_at_decision").HasColumnType("bigint").IsRequired();
        builder.Property(d => d.ResultRevisionId).HasColumnName("result_revision_id").HasColumnType("uuid");
        builder.Property(d => d.ModificationLevel).HasColumnName("modification_level").HasColumnType("varchar(30)").IsRequired();

        builder.Property(d => d.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(d => d.CreatedBy).HasColumnName("created_by").HasColumnType("varchar(100)");
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
        builder.Property(d => d.UpdatedBy).HasColumnName("updated_by").HasColumnType("varchar(100)");

        builder.Ignore(d => d.IsDeleted);
        builder.Ignore(d => d.DeletedAt);
        builder.Ignore(d => d.DeletedBy);

        builder.HasIndex(d => d.SuggestionId).HasDatabaseName("ix_ai_suggestion_decisions_suggestion");
        // Một đề xuất chỉ có tối đa một quyết định Accepted (chặn Accept hai lần song song)
        builder.HasIndex(d => d.SuggestionId).IsUnique().HasFilter("decision = 'Accepted'")
               .HasDatabaseName("ux_ai_suggestion_decisions_accepted");

        builder.HasOne<AiSuggestion>().WithMany().HasForeignKey(d => d.SuggestionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ContentRevision>().WithMany().HasForeignKey(d => d.ResultRevisionId).OnDelete(DeleteBehavior.Restrict);
    }
}