using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Admin.Models;
using PanelForge.Application.Features.AiAgents.Models;
using PanelForge.Infrastructure.Persistence;

namespace PanelForge.Infrastructure.Services;

/// <summary>
/// OPS-32: mỗi thành phần gọi thử một lần, có timeout riêng; lỗi chỉ ghi kiểu exception, không ghi chuỗi kết nối.
/// Chậm hơn <see cref="SlowMs"/> thì báo Degraded.
/// </summary>
public sealed class SystemHealthProbe(
    PanelForgeDbContext db,
    IAssetStorage storage,
    IHttpClientFactory httpFactory,
    AiAgentSettings aiSettings,
    IConfiguration configuration,
    IEmailService email,
    ILogger<SystemHealthProbe> logger) : ISystemHealthProbe
{
    public const string AiHealthHttpClient = "ai-health";
    private const int SlowMs = 1500;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public async Task<IReadOnlyList<DependencyHealthDto>> CheckDependenciesAsync(CancellationToken cancellationToken = default)
    {
        // Tuần tự: DbContext không cho chạy song song, và hai lượt còn lại đều có timeout ngắn.
        return
        [
            await ProbeAsync("database", "PostgreSQL", async ct =>
            {
                if (!await db.Database.CanConnectAsync(ct)) throw new InvalidOperationException("CanConnect = false");
                return (int?)null;
            }, cancellationToken),
            await ProbeAsync("storage", configuration["AssetStorage:Provider"] is { Length: > 0 } p ? p : "Local", async ct =>
            {
                await foreach (var _ in storage.ListAsync("", ct)) break;
                return (int?)null;
            }, cancellationToken),
            await ProbeAiAsync(cancellationToken),
            await ProbeEmailAsync(cancellationToken)
        ];
    }

    /// <summary>
    /// Không kết nối SMTP mỗi lần mở trang (chậm, có nơi chặn đăng nhập dồn dập): chỉ xem cấu hình có đủ không
    /// và lần gửi gần nhất trong 24h có lỗi không. Gửi thử thật ở Admin → Cấu hình email.
    /// </summary>
    private async Task<DependencyHealthDto> ProbeEmailAsync(CancellationToken cancellationToken)
    {
        try
        {
            var status = await email.GetConfigurationStatusAsync(cancellationToken);
            var provider = status.SmtpServer is null ? "SMTP" : $"{status.SmtpServer}:{status.SmtpPort}";
            if (!status.IsConfigured)
                return new DependencyHealthDto("email", HealthStatus.Down, provider, null, status.Problem, null);

            var dayAgo = DateTime.UtcNow.AddDays(-1);
            var last = await db.EmailDeliveryLogs.AsNoTracking()
                .Where(l => l.CreatedAt >= dayAgo)
                .OrderByDescending(l => l.CreatedAt)
                .Select(l => new { l.Succeeded, l.FailureReason })
                .FirstOrDefaultAsync(cancellationToken);
            return last is { Succeeded: false }
                ? new DependencyHealthDto("email", HealthStatus.Degraded, provider, null, last.FailureReason, null)
                : new DependencyHealthDto("email", HealthStatus.Healthy, provider, null, null, null);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("System health: email lỗi ({Type})", ex.GetType().Name);
            return new DependencyHealthDto("email", HealthStatus.Down, "SMTP", null, "UNREACHABLE", null);
        }
    }

    private async Task<DependencyHealthDto> ProbeAiAsync(CancellationToken cancellationToken)
    {
        var result = await ProbeAsync("ai", "PanelForge.AI", async ct =>
        {
            var http = httpFactory.CreateClient(AiHealthHttpClient);
            var body = await http.GetFromJsonAsync<AiHealthBody>("health/ai", ct);
            return body?.QueueDepth;
        }, cancellationToken);

        // Service sống nhưng Backend chưa có khoá dịch vụ: mọi job AI sẽ bị từ chối.
        return result.Status == HealthStatus.Healthy && string.IsNullOrWhiteSpace(aiSettings.ServiceKey)
            ? result with { Status = HealthStatus.Degraded, Reason = "SERVICE_KEY_MISSING" }
            : result;
    }

    private async Task<DependencyHealthDto> ProbeAsync(
        string key, string provider, Func<CancellationToken, Task<int?>> check, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Timeout);
        var watch = Stopwatch.StartNew();
        try
        {
            var queueDepth = await check(cts.Token);
            var ms = (int)watch.ElapsedMilliseconds;
            return ms > SlowMs
                ? new DependencyHealthDto(key, HealthStatus.Degraded, provider, ms, "SLOW", queueDepth)
                : new DependencyHealthDto(key, HealthStatus.Healthy, provider, ms, null, queueDepth);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("System health: {Key} lỗi ({Type})", key, ex.GetType().Name);
            var reason = ex is OperationCanceledException ? "TIMEOUT" : "UNREACHABLE";
            return new DependencyHealthDto(key, HealthStatus.Down, provider, null, reason, null);
        }
    }

    private sealed record AiHealthBody(string? Status, int QueueDepth);
}
