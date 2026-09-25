namespace PanelForge.Domain.Enums;

/// <summary>
/// Platform-level role gán trực tiếp trên bảng users.
/// Chỉ có 2 giá trị:
///   - User  : người dùng thông thường (mặc định khi đăng ký).
///   - Admin : quản trị viên toàn hệ thống, bypass mọi workspace authorization check.
///
/// Quyền sản xuất chi tiết (Producer, Writer, Artist, Letterer, Editor, Reviewer)
/// được quản lý qua WorkspaceRole trong bảng workspace_members.
/// </summary>
public enum SystemRole
{
    User,
    Admin
}