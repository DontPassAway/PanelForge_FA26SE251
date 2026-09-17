using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PanelForge.Application.Interfaces;
using PanelForge.Application.Interfaces.Authentication;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Application.Services;
using PanelForge.Infrastructure.Authentication;
using PanelForge.Infrastructure.Firebase;
using PanelForge.Infrastructure.Persistence;
using PanelForge.Infrastructure.Services;

namespace PanelForge.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException(
                "Connection string 'PostgreSQL' không được cấu hình trong appsettings.json.");

        services.AddDbContext<PanelForgeDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.EnableRetryOnFailure(maxRetryCount: 5);
                npgsql.MigrationsAssembly(typeof(PanelForgeDbContext).Assembly.FullName);
            });

#if DEBUG
            options.EnableSensitiveDataLogging()
                   .EnableDetailedErrors();
#endif
        });

        services.AddScoped<IPanelForgeDbContext>(
            provider => provider.GetRequiredService<PanelForgeDbContext>());

        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();

        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IWorkspaceAuthorizationService, WorkspaceAuthorizationService>();
        services.AddScoped<IWorkspaceService, WorkspaceService>();

        services.AddFirebaseServices(configuration);

        return services;
    }
}
