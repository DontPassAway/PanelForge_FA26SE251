using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace PanelForge.Infrastructure.Persistence;

public sealed class PanelForgeDbContextFactory : IDesignTimeDbContextFactory<PanelForgeDbContext>
{
    public PanelForgeDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "..", "PanelForge.API"))
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var connectionString = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException("Connection string 'PostgreSQL' not found.");

        var optionsBuilder = new DbContextOptionsBuilder<PanelForgeDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsAssembly(typeof(PanelForgeDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention(); // B?t bu?c ph?i có dòng này ?? design-time sinh code chu?n snake_case

        return new PanelForgeDbContext(optionsBuilder.Options);
    }
}