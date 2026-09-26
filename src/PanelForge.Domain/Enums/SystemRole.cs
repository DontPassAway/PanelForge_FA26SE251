namespace PanelForge.Domain.Enums;

/// <summary>
/// Platform-level role gán trực tiếp trên bảng users (E-11).
/// Chỉ có 2 giá trị:
///   - User  : người dùng thông thường (mặc định khi đăng ký).
///   - Admin : quản trị viên hệ thống. Quản lý tài khoản, CanCreateStudio, cấu hình AI và audit log;
///             KHÔNG có WorkspaceRole và KHÔNG có quyền tạo/sửa nội dung trong bất kỳ Studio nào (BR-07).
///
/// Quyền sản xuất chi tiết (Producer, Writer, Artist, Letterer, Editor)
/// được quản lý qua WorkspaceRole trong bảng workspace_members.
/// </summary>
public enum SystemRole
{
    User,
    Admin
}
