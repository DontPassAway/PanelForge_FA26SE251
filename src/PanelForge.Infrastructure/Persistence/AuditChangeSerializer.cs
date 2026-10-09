using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace PanelForge.Infrastructure.Persistence;

/// <summary>
/// Sinh ChangesJson cho AuditLog (UC-15):
/// - CREATE: { "Prop": giá trị mới, ... }
/// - UPDATE: { "Prop": { "old": ..., "new": ... } } chỉ với các cột thật sự đổi
/// - DELETE: null (entity id đã có trong bản ghi audit)
/// Không bao giờ ghi giá trị của cột nhạy cảm (mật khẩu, token, secret, API key) — chỉ ghi "***".
/// </summary>
internal static class AuditChangeSerializer
{
    private const int MaxValueLength = 500;

    // So khớp theo tên cột: PasswordHash, PasswordResetToken, EmailVerificationToken, TwoFactorSecret,
    // ApiKeyEncrypted, TokenHash. Không dùng "token" chung vì sẽ che MonthlyTokenQuota/UsedTokensCurrentMonth.
    private static readonly string[] SensitiveMarkers =
        ["password", "secret", "apikey", "tokenhash"];

    private static readonly HashSet<string> SkippedProperties =
        ["CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string? Serialize(EntityEntry entry)
    {
        var changes = new Dictionary<string, object?>();

        switch (entry.State)
        {
            case EntityState.Added:
                foreach (var p in entry.Properties)
                {
                    if (Skip(p)) continue;
                    changes[p.Metadata.Name] = Mask(p.Metadata.Name, p.CurrentValue);
                }
                break;

            case EntityState.Modified:
                foreach (var p in entry.Properties)
                {
                    if (!p.IsModified || Skip(p) || Equals(p.OriginalValue, p.CurrentValue)) continue;
                    changes[p.Metadata.Name] = new
                    {
                        old = Mask(p.Metadata.Name, p.OriginalValue),
                        @new = Mask(p.Metadata.Name, p.CurrentValue)
                    };
                }
                break;

            default:
                return null;
        }

        return changes.Count == 0 ? null : JsonSerializer.Serialize(changes, JsonOptions);
    }

    private static bool Skip(PropertyEntry p)
        => p.Metadata.IsPrimaryKey() || SkippedProperties.Contains(p.Metadata.Name);

    internal static bool IsSensitive(string propertyName)
    {
        var name = propertyName.ToLowerInvariant();
        return SensitiveMarkers.Any(name.Contains) || name.EndsWith("token");
    }

    private static object? Mask(string propertyName, object? value)
    {
        if (value is null) return null;
        if (IsSensitive(propertyName)) return "***";
        if (value is string s && s.Length > MaxValueLength) return s[..MaxValueLength] + "…";
        return value;
    }
}
