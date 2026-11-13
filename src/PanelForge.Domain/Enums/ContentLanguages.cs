namespace PanelForge.Domain.Enums;

/// <summary>
/// Ngôn ngữ bản phát hành cho Reader. Bản Studio sản xuất luôn là <see cref="Original"/> (tiếng Việt);
/// các ngôn ngữ còn lại là bản dịch do Publisher upload ảnh từng trang. Mua chương/trang theo từng ngôn ngữ.
/// </summary>
public static class ContentLanguages
{
    public const string Vietnamese = "vi";
    public const string English = "en";
    public const string Japanese = "ja";

    /// <summary>Ngôn ngữ của bản Studio sản xuất.</summary>
    public const string Original = Vietnamese;

    public static readonly IReadOnlyList<string> All = [Vietnamese, English, Japanese];

    public static readonly IReadOnlyList<string> Translations = [English, Japanese];

    public static bool IsSupported(string? language) =>
        language is not null && All.Contains(language.Trim().ToLowerInvariant());

    /// <summary>Chuẩn hóa mã ngôn ngữ; null/rỗng → ngôn ngữ gốc. Mã không hỗ trợ trả null.</summary>
    public static string? Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return Original;
        var code = language.Trim().ToLowerInvariant();
        return All.Contains(code) ? code : null;
    }
}
