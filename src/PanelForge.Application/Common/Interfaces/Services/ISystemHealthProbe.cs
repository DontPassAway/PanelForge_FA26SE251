using PanelForge.Application.Features.Admin.Models;

namespace PanelForge.Application.Common.Interfaces.Services;

/// <summary>
/// Gọi thử các thành phần hạ tầng (DB, kho file, service AI) cho trang System Health của Admin (OPS-32).
/// Là port vì phải đi qua mạng/I-O thật và thay được trong test. Không bao giờ ném lỗi vì một thành phần hỏng:
/// thành phần đó trả <see cref="HealthStatus.Down"/> kèm mô tả ngắn, không lộ chuỗi kết nối hay khoá.
/// </summary>
public interface ISystemHealthProbe
{
    Task<IReadOnlyList<DependencyHealthDto>> CheckDependenciesAsync(CancellationToken cancellationToken = default);
}
