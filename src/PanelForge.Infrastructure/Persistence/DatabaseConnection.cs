using Microsoft.Extensions.Configuration;

namespace PanelForge.Infrastructure.Persistence;

/// <summary>
/// Chọn chuỗi kết nối PostgreSQL theo <c>Database:Target</c>:
///   "Docker" → ConnectionStrings:PostgreSQLDocker (container, mặc định cổng 5433)
///   "Local" hoặc bỏ trống → ConnectionStrings:PostgreSQL (PostgreSQL cài trên máy, cổng 5432)
/// Đổi nhanh bằng launch profile (http-docker / http-local) hoặc biến môi trường Database__Target.
/// </summary>
public static class DatabaseConnection
{
    public const string LocalKey = "PostgreSQL";
    public const string DockerKey = "PostgreSQLDocker";

    public static string Resolve(IConfiguration configuration)
    {
        var target = configuration["Database:Target"];
        var useDocker = string.Equals(target, "Docker", StringComparison.OrdinalIgnoreCase);
        var key = useDocker ? DockerKey : LocalKey;

        return configuration.GetConnectionString(key)
            ?? throw new InvalidOperationException(
                $"Connection string '{key}' chưa được cấu hình (Database:Target = '{target ?? "Local"}'). " +
                "Thêm vào appsettings.Development.json, xem appsettings.Example.json.");
    }
}
