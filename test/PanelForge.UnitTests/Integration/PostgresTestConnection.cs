using Npgsql;

namespace PanelForge.UnitTests.Integration;

/// <summary>
/// Connection string cho các integration test chạy trên Postgres thật.
/// Mặc định khớp service Postgres trong <c>.github/workflows/ci.yml</c>; chạy local thì đặt biến môi trường
/// <c>PANELFORGE_TEST_POSTGRES</c> (connection string tới database <c>postgres</c>, user có quyền CREATE DATABASE).
/// </summary>
internal static class PostgresTestConnection
{
    private const string CiDefault = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=password123";

    public static string Admin { get; } =
        Environment.GetEnvironmentVariable("PANELFORGE_TEST_POSTGRES") is { Length: > 0 } fromEnv ? fromEnv : CiDefault;

    public static string ForDatabase(string databaseName) =>
        new NpgsqlConnectionStringBuilder(Admin) { Database = databaseName }.ConnectionString;
}
