namespace PanelForge.Domain.Enums;

/// <summary>
/// Platform-level role gán trực tiếp trên bảng users (E-11).
/// Gồm:
///   - User      : người dùng thông thường (mặc định khi đăng ký).
///   - Admin     : quản trị viên hệ thống. Quản lý tài khoản, CanCreateStudio, cấu hình AI và audit log;
///                 KHÔNG có WorkspaceRole và KHÔNG có quyền tạo/sửa nội dung trong bất kỳ Studio nào (BR-07).
///   - Publisher : vai trò xuất bản độc lập cấp hệ thống. Quản lý cổng xuất bản (/publisher),
///                 duyệt phát hành hoặc từ chối các chương đã hoàn thiện từ mọi Studio mà không cần
///                 phải là thành viên trong Workspace của Studio đó.
/// </summary>
public enum SystemRole
{
    User,
    Admin,
    Publisher
}
