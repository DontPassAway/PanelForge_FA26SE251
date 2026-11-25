namespace PanelForge.Domain.Entities.Identity;

/// <summary>
/// Danh mục ID bìa hồ sơ mẫu của hệ thống (ảnh nằm ở Frontend: public/presets/profile-covers).
/// Thêm mẫu mới: thêm ID ở đây và ở danh mục Frontend (profileCovers.ts).
/// </summary>
public static class ProfileCoverPresets
{
    public static readonly IReadOnlySet<string> Ids = new HashSet<string>(StringComparer.Ordinal)
    {
        "profile-cover-manga-studio",
        "profile-cover-green-workspace",
        "profile-cover-robot-studio",
        "profile-cover-sunny-loft",
        "profile-cover-sketch-desk",
        "profile-cover-cozy-orange",
    };

    public static bool IsValid(string presetId) => Ids.Contains(presetId);
}
