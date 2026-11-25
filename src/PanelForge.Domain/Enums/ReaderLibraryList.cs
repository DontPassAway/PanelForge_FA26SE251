namespace PanelForge.Domain.Enums;

/// <summary>Danh sách cá nhân của độc giả trên Mobile (đồng bộ giữa các thiết bị).</summary>
public enum ReaderLibraryList
{
    /// <summary>Đọc sau (bookmark).</summary>
    ReadLater = 1,

    /// <summary>Theo dõi — báo khi bộ truyện có chương mới.</summary>
    Following = 2,

    /// <summary>Yêu thích.</summary>
    Favorite = 3,
}
