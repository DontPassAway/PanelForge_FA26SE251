using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PanelForge.Domain.Entities.Ai;

namespace PanelForge.Infrastructure.Persistence.Configurations.Ai;

internal sealed class AiProviderCredentialConfiguration : IEntityTypeConfiguration<AiProviderCredential>
{
    public void Configure(EntityTypeBuilder<AiProviderCredential> builder)
    {
        builder.ToTable("ai_provider_credentials");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(c => c.Name).HasColumnName("name").HasColumnType("varchar(100)").IsRequired();
        builder.Property(c => c.ProviderType).HasColumnName("provider_type").HasColumnType("varchar(50)").IsRequired();
        builder.Property(c => c.AccountId).HasColumnName("account_id").HasColumnType("varchar(100)");
        builder.Property(c => c.ApiKeyEncrypted).HasColumnName("api_key_encrypted").HasColumnType("text").IsRequired();
        builder.Property(c => c.TextModel).HasColumnName("text_model").HasColumnType("varchar(200)").IsRequired();
        builder.Property(c => c.ImageModel).HasColumnName("image_model").HasColumnType("varchar(200)");
        builder.Property(c => c.IsEnabled).HasColumnName("is_enabled").HasDefaultValue(true).IsRequired();
    }
}
