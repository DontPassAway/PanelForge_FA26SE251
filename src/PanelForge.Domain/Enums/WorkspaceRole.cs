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
    Publisher,
    /// <summary>
    /// Thành viên Studio chưa có vai trò chuyên môn (spec D10). Vai trò Writer/Artist/Letterer/Editor chỉ gán theo
    /// từng Series (<c>series_members.role</c>); ở cấp Studio chỉ có Producer hoặc Member.
    /// </summary>
    Member
}
