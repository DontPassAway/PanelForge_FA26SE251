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

        if (File.Exists(Path.Combine(currentDir, "appsettings.json")))
        {
            apiPath = currentDir;
        }
        else if (File.Exists(Path.Combine(currentDir, "src", "PanelForge.API", "appsettings.json")))
        {
            apiPath = Path.Combine(currentDir, "src", "PanelForge.API");
        }
        else if (File.Exists(Path.Combine(currentDir, "..", "PanelForge.API", "appsettings.json")))
        {
            apiPath = Path.Combine(currentDir, "..", "PanelForge.API");
        }
        else
        {
            apiPath = Path.Combine(currentDir, "src", "PanelForge.API");
        }

        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiPath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException("Connection string 'PostgreSQL' not found.");

        var optionsBuilder = new DbContextOptionsBuilder<PanelForgeDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsAssembly(typeof(PanelForgeDbContext).Assembly.FullName));

        return new PanelForgeDbContext(optionsBuilder.Options);
    }
}