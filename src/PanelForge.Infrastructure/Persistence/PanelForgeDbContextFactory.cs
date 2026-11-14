using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace PanelForge.Infrastructure.Persistence;

public sealed class PanelForgeDbContextFactory : IDesignTimeDbContextFactory<PanelForgeDbContext>
{
    public PanelForgeDbContext CreateDbContext(string[] args)
    {
        var currentDir = Directory.GetCurrentDirectory();
        var apiPath = currentDir;

        if (File.Exists(Path.Combine(currentDir, "appsettings.json")) || File.Exists(Path.Combine(currentDir, "appsettings.Example.json")))
        {
            apiPath = currentDir;
        }
        else if (File.Exists(Path.Combine(currentDir, "src", "PanelForge.API", "appsettings.Example.json")))
        {
            apiPath = Path.Combine(currentDir, "src", "PanelForge.API");
        }
        else if (File.Exists(Path.Combine(currentDir, "..", "PanelForge.API", "appsettings.Example.json")))
        {
            apiPath = Path.Combine(currentDir, "..", "PanelForge.API");
        }
        else
        {
            apiPath = Path.Combine(currentDir, "src", "PanelForge.API");
        }

        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiPath)
            .AddJsonFile("appsettings.Example.json", optional: true)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        string connectionString;
        try
        {
            connectionString = DatabaseConnection.Resolve(configuration);
        }
        catch
        {
            connectionString = "Host=localhost;Database=panelforge_dev;Username=postgres;Password=postgres";
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Host=localhost;Database=panelforge_dev;Username=postgres;Password=postgres";
        }

        var optionsBuilder = new DbContextOptionsBuilder<PanelForgeDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsAssembly(typeof(PanelForgeDbContext).Assembly.FullName));

        return new PanelForgeDbContext(optionsBuilder.Options);
    }
}