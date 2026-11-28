using Microsoft.AspNetCore.DataProtection;

using JasperFx;
using Marten;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Models;
using PanelForge.Application.Features.Payments.Models;
using PanelForge.Application.Features.Workspaces.Models;
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
using PanelForge.Infrastructure.ExternalServices.GooglePlay;
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

            options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
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
                typeof(ElementTranslationUpdatedEvent),
                typeof(ElementOverflowAcknowledgedEvent),
                typeof(ElementReadingOrderSetEvent),
                typeof(ElementStyleUpdatedEvent),
                typeof(ElementRestoredEvent),
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

        // Google Play Billing: xác thực purchaseToken với Google Play Developer API trước khi cộng Ví.
        services.AddHttpClient(GooglePlayPurchaseVerifier.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddSingleton<IGooglePlayPurchaseVerifier, GooglePlayPurchaseVerifier>();

        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<IWorkspaceAuthorizationService, WorkspaceAuthorizationService>();
        services.AddScoped<IChapterCastInheritanceResolver, ChapterCastInheritanceResolver>();

        // ── Artwork assets: lưu file cục bộ + link tải ngắn hạn ký bằng Data Protection (BR-04, BR-13, BR-16) ──
        // AssetStorage:Provider = "Local" (mặc định, đĩa cục bộ) | "R2" (Cloudflare R2)
        if (string.Equals(configuration["AssetStorage:Provider"], "R2", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IAssetStorage>(_ => R2AssetStorage.FromConfiguration(configuration));
        else
            services.AddSingleton<IAssetStorage, LocalAssetStorage>();
        services.AddSingleton<IAssetLinkSigner, DataProtectionAssetLinkSigner>();

        // ── UC-12: dựng file xuất chương (SkiaSharp) ──
        services.AddSingleton<IChapterExportRenderer, PanelForge.Infrastructure.Exports.SkiaChapterExportRenderer>();
        services.AddSingleton<IImageThumbnailer, PanelForge.Infrastructure.Exports.SkiaImageThumbnailer>();
        services.AddSingleton<IPageLetteringCompositor, PanelForge.Infrastructure.Exports.SkiaPageLetteringCompositor>();

        // ── AI Configuration & Security (UC-02, NFR-03) ────────────────────────
        // TASK-352: khi deploy đặt DataProtection__KeysPath (volume) để khoá giải mã API key AI / link asset
        // sống qua lần dựng lại container. SetApplicationName cố định discriminator để không phụ thuộc ContentRootPath.
        var dp = services.AddDataProtection().SetApplicationName("PanelForge");
        DirectoryInfo? dpKeysDir = null;
        if (configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
        {
            dpKeysDir = new DirectoryInfo(keysPath);
            dp.PersistKeysToFileSystem(dpKeysDir);
        }
        services.AddSingleton<IAiKeyProtector>(sp =>
        {
            var env = sp.GetService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
            IDataProtector? legacy = null;
            if (env?.ContentRootPath is { Length: > 0 } contentRoot)
            {
                var legacyProvider = dpKeysDir is not null
                    ? DataProtectionProvider.Create(dpKeysDir, b => b.SetApplicationName(contentRoot))
                    : DataProtectionProvider.Create(contentRoot);
                legacy = legacyProvider.CreateProtector(AiKeyProtector.Purpose);
            }
            return new AiKeyProtector(
                sp.GetRequiredService<IDataProtectionProvider>(),
                sp.GetService<Microsoft.Extensions.Logging.ILogger<AiKeyProtector>>(),
                legacy);
        });
        services.AddScoped<IAiProviderValidator, AiProviderValidator>();
        services.AddHttpClient(AiProviderValidator.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<IAiUsageService, AiUsageService>();
        services.AddScoped<IImageConsistencyService, ImageConsistencyService>();

        // ── Tách panel/bong bóng từ ảnh cả trang: OpenCV chạy trong tiến trình (thay service Python 5066) ──
        services.AddSingleton<IPanelSplitter, OpenCvPanelSplitter>();

        // ── Thanh toán VietQR: tài khoản nhận tiền mặc định khi Studio chưa khai ngân hàng ──
        var defaultBank = new DefaultBankSettings();
        configuration.GetSection(DefaultBankSettings.SectionName).Bind(defaultBank);
        services.AddSingleton(defaultBank);

        // ── Hạn mức cấp cho Studio mới (OPS-01, BR-22): server tự đặt, client không gửi ──
        var workspaceDefaults = new WorkspaceDefaultsSettings();
        configuration.GetSection(WorkspaceDefaultsSettings.SectionName).Bind(workspaceDefaults);
        if (workspaceDefaults.StorageQuotaBytes < 0 || workspaceDefaults.MonthlyTokenQuota < 0 || workspaceDefaults.MonthlyImageQuota < 0)
            throw new InvalidOperationException($"{WorkspaceDefaultsSettings.SectionName}: hạn mức mặc định không được âm.");
        services.AddSingleton(workspaceDefaults);

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

        // Copilot (sync): KHÔNG retry (mỗi lần gọi tốn token, không idempotent), timeout cứng theo CopilotTimeoutSeconds
        services.AddHttpClient(AiClient.CopilotHttpClient, client =>
        {
            client.BaseAddress = new Uri(aiSettings.ServiceUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(aiSettings.CopilotTimeoutSeconds, 2, 60));
        });
        // Sinh ảnh (sync): KHÔNG retry (mỗi ảnh tốn quota), timeout dài hơn timeout phía AI service
        services.AddHttpClient(AiClient.ImageHttpClient, client =>
        {
            client.BaseAddress = new Uri(aiSettings.ServiceUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(aiSettings.ImageTimeoutSeconds, 15, 200));
        });

        // ── Dịch kịch bản / thoại (LibreTranslate tự lưu trữ) ──
        var translationBaseUrl = configuration["TranslationService:BaseUrl"] ?? "http://localhost:5000";
        services.AddHttpClient<ITranslationService, LibreTranslateService>(client =>
        {
            client.BaseAddress = new Uri(translationBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        // ── Audit trail: danh tính người gọi request (BR-18, UC-15) ─────────────
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, HttpCurrentUserService>();

        services.AddFirebaseServices(configuration);

        return services;
    }
}
