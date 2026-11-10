using System;

namespace PanelForge.Domain.Enums;

public enum WorkspaceRole
{
    Producer,
    Writer,
    Artist,
    Letterer,
    Editor,
    /// <summary>
    /// [Obsolete] Đã được chuyển lên SystemRole.Publisher (cổng xuất bản độc lập). Giữ lại để tương thích ngược dữ liệu DB cũ.
    /// </summary>
    [Obsolete("Use SystemRole.Publisher instead")]
    Publisher
}
