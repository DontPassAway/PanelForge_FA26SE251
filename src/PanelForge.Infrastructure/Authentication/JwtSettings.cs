namespace PanelForge.Infrastructure.Authentication;

public class JwtSettings
{
    public const string SectionName = "JwtSettings";

    public string Secret { get; set; } = default!;
    public string Issuer { get; set; } = default!;
    public string Audience { get; set; } = default!;
    public int ExpiryMinutes { get; set; } = 1440;
}
