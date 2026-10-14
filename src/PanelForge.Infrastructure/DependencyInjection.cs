
using JasperFx;
using Marten;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Models;
using PanelForge.Infrastructure.ExternalServices;
using PanelForge.Application.Common.Interfaces.Authentication;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Events;
using PanelForge.Infrastructure.Authentication;
using PanelForge.Infrastructure.ExternalServices.Email;
using PanelForge.Infrastructure.ExternalServices.Firebase;
using PanelForge.Infrastructure.Persistence;
using PanelForge.Infrastructure.Persistence.EventStore.Projections;
using PanelForge.Infrastructure.Persistence.EventStore;
using PanelForge.Infrastructure.Services;
using Weasel.Core;

namespace PanelForge.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = DatabaseConnection.Resolve(configuration);

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
                typeof(ElementRemovedEvent),
                typeof(ElementTextUpdatedEvent),
                typeof(ElementOverflowAcknowledgedEvent),
                typeof(ElementReadingOrderSetEvent),
                typeof(PageDeletedEvent)
            });

            // Đăng ký PageProjection tường minh (SingleStreamProjection)
            opts.Projections.Add<PageProjection>(JasperFx.Events.Projections.ProjectionLifecycle.Live);

            // Development: tự tạo schema nếu chưa có (mt_events table, ...)
            opts.AutoCreateSchemaObjects = AutoCreate.All;
        })
        .UseLightweightSessions();

        // ── Repositories ───────────────────────────────────────────────────────
        services.AddScoped<IPageRepository, MartenPageRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // ── Auth infrastructure ────────────────────────────────────────────────
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddSingleton<ITotpService, OtpNetTotpService>();
        services.AddSingleton<IExternalIdentityVerifier, FirebaseIdentityVerifier>();

        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<IWorkspaceAuthorizationService, WorkspaceAuthorizationService>();

        // ── Artwork assets: lưu file cục bộ + link tải ngắn hạn ký bằng Data Protection (BR-04, BR-13, BR-16) ──
        // AssetStorage:Provider = "Local" (mặc định, đĩa cục bộ) | "R2" (Cloudflare R2)
        if (string.Equals(configuration["AssetStorage:Provider"], "R2", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IAssetStorage>(_ => R2AssetStorage.FromConfiguration(configuration));
        else
            services.AddSingleton<IAssetStorage, LocalAssetStorage>();
        services.AddSingleton<IAssetLinkSigner, DataProtectionAssetLinkSigner>();

        // ── UC-12: dựng file xuất chương (SkiaSharp) ──
        services.AddSingleton<IChapterExportRenderer, PanelForge.Infrastructure.Exports.SkiaChapterExportRenderer>();

        // ── AI Configuration & Security (UC-02, NFR-03) ────────────────────────
        services.AddDataProtection();
        services.AddSingleton<IAiKeyProtector, AiKeyProtector>();
        services.AddScoped<IAiProviderValidator, AiProviderValidator>();
        services.AddScoped<IAiUsageService, AiUsageService>();

        // ── AI Multi-Agent: client mỏng sang service PanelForge.AI (cách ly lỗi: timeout + retry + circuit breaker) ──
        var aiSettings = new AiAgentSettings();
        configuration.GetSection(AiAgentSettings.SectionName).Bind(aiSettings);
        services.AddSingleton(aiSettings);
        services.AddHttpClient<IAiClient, AiClient>(client =>
            {
                client.BaseAddress = new Uri(aiSettings.ServiceUrl.TrimEnd('/') + "/");
            })
            .AddStandardResilienceHandler(o =>
            {
                var timeout = TimeSpan.FromSeconds(Math.Max(2, aiSettings.ServiceTimeoutSeconds));
                o.AttemptTimeout.Timeout = timeout;
                o.TotalRequestTimeout.Timeout = timeout * 2 + TimeSpan.FromSeconds(2);
                o.Retry.MaxRetryAttempts = 1;               // giao job idempotent theo runId phía AI service
                o.CircuitBreaker.SamplingDuration = timeout * 2 + TimeSpan.FromSeconds(1);
                o.CircuitBreaker.MinimumThroughput = 3;     // 3 lần lỗi → mở mạch, trả AI_UNAVAILABLE ngay
                o.CircuitBreaker.FailureRatio = 0.5;
                o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(20);
            });

        // ── Audit trail: danh tính người gọi request (BR-18, UC-15) ─────────────
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, HttpCurrentUserService>();

        services.AddFirebaseServices(configuration);

        return services;
    }
}
