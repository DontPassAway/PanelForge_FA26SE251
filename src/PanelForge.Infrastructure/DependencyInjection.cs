
using JasperFx;
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
using PanelForge.Infrastructure.Persistence.Projections;
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
        services.AddMarten((StoreOptions opts) =>
        {
            opts.Connection(connectionString);

            // Đăng ký Domain Events để Marten biết cách serialize/deserialize JSON
            opts.Events.AddEventTypes(new[]
            {
                typeof(PageCreatedEvent),
                typeof(PageCanvasUpdatedEvent),
                typeof(PageReorderedEvent),
                typeof(ElementAddedEvent),
                typeof(ElementMovedEvent),
                typeof(ElementRemovedEvent)
            });

            // Đăng ký PageProjection tường minh (SingleStreamProjection)
            opts.Projections.Add<PageProjection>(JasperFx.Events.Projections.ProjectionLifecycle.Live);

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
        services.AddScoped<IWorkspaceAuthorizationService, WorkspaceAuthorizationService>();
        services.AddScoped<IWorkspaceService, WorkspaceService>();

        // ── AI Configuration & Security (UC-02, NFR-03) ────────────────────────
        services.AddDataProtection();
        services.AddSingleton<IAiKeyProtector, AiKeyProtector>();
        services.AddScoped<IAiProviderValidator, AiProviderValidator>();
        services.AddScoped<IAiUsageService, AiUsageService>();

        // ── Audit trail: danh tính người gọi request (BR-18, UC-15) ─────────────
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, HttpCurrentUserService>();

        services.AddFirebaseServices(configuration);

        return services;
    }
}
