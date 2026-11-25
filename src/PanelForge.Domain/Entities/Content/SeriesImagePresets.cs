namespace PanelForge.Domain.Entities.Content;

/// <summary>
/// Danh mục ảnh mẫu cho avatar/banner Series. File nằm ở API: <c>Presets/series/{avatars|banners}/{id}.webp</c>
/// (Backend tự đọc, không nhận URL/đường dẫn từ client). Thêm mẫu: thêm file + ID ở đây + danh mục Frontend.
/// </summary>
public static class SeriesImagePresets
{
    public static readonly IReadOnlySet<string> AvatarIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "series-avatar-a1", "series-avatar-a2", "series-avatar-a3",
        "series-avatar-a4", "series-avatar-a5", "series-avatar-a6",
    };

    public static readonly IReadOnlySet<string> BannerIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "series-banner-manga-studio", "series-banner-green-workspace", "series-banner-robot-studio",
        "series-banner-sunny-loft", "series-banner-sketch-desk", "series-banner-cozy-orange",
    };
}
