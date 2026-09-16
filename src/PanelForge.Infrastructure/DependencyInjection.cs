using Marten;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PanelForge.Application.Interfaces;
using PanelForge.Application.Interfaces.Authentication;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Application.Services;
using PanelForge.Domain.Events;
using PanelForge.Infrastructure.Authentication;
using PanelForge.Infrastructure.Firebase;
using PanelForge.Infrastructure.Persistence;
using PanelForge.Infrastructure.Repositories;
using PanelForge.Infrastructure.Services;
using Weasel.Core;

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

        // ── EF Core (Auth entities: Users, Workspaces, Series, ...) ──────────
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

        // ── Marten Event Store (Page aggregate + Event Sourcing) ───────────────
        services.AddMarten(opts =>
        {
            opts.Connection(connectionString);

            // Đăng ký Domain Events để Marten biết cách serialize/deserialize JSON
            opts.Events.AddEventTypes(new[]
            {
                typeof(ElementAddedEvent),
                typeof(ElementMovedEvent),
                typeof(ElementRemovedEvent)
            });

            // Development: tự tạo schema nếu chưa có (mt_events table, ...)
            opts.AutoCreateSchemaObjects = AutoCreate.All;
        })
        .UseLightweightSessions();

        // ── Repositories ───────────────────────────────────────────────────────
        services.AddScoped<IPageRepository, MartenPageRepository>();

        // ── Auth infrastructure ────────────────────────────────────────────────
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();

        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<IAuthService, AuthService>();

        services.AddFirebaseServices(configuration);

        return services;
    }
}
