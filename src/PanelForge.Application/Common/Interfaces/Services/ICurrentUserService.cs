namespace PanelForge.Application.Interfaces;

/// <summary>
/// Danh tính của người đang gọi request (từ JWT). Dùng cho audit trail (BR-18, UC-15).
/// Mọi giá trị là null khi không có HTTP request (background job, migration, seeder).
/// </summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? Email { get; }
    string? IpAddress { get; }
}
