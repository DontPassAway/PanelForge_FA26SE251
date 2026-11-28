using System.Diagnostics;
using System.Reflection;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Admin.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Admin.Queries.GetSystemHealth;

/// <summary>
/// GET /api/admin/system-health (OPS-32): gọi thử DB, kho file, service AI và đếm việc nền bị kẹt hoặc lỗi từ DB.
/// Ngưỡng "kẹt" bám theo thời gian một việc bình thường cần: run AI im lặng 15 phút, export chờ 30 phút.
/// </summary>
public sealed record GetSystemHealthQuery : IRequest<Result<SystemHealthDto>>;

public sealed class GetSystemHealthQueryHandler(IPanelForgeDbContext db, ISystemHealthProbe probe)
    : IRequestHandler<GetSystemHealthQuery, Result<SystemHealthDto>>
{
    internal static readonly TimeSpan AiRunStuckAfter = TimeSpan.FromMinutes(15);
    internal static readonly TimeSpan ExportStuckAfter = TimeSpan.FromMinutes(30);
    internal static readonly TimeSpan TopUpWaitWarning = TimeSpan.FromHours(24);
    internal static readonly TimeSpan QuotaRequestWaitWarning = TimeSpan.FromDays(3);

    private static readonly DateTime ServerStartedAt = Process.GetCurrentProcess().StartTime.ToUniversalTime();
    private static readonly string? Version = Assembly.GetEntryAssembly()
        ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    public async Task<Result<SystemHealthDto>> Handle(GetSystemHealthQuery q, CancellationToken ct)
    {
        var dependencies = await probe.CheckDependenciesAsync(ct);
        var now = DateTime.UtcNow;
        var dayAgo = now.AddDays(-1);

        var signals = new List<OperationalSignalDto>();
        // Nếu DB hỏng thì không đếm được gì: chỉ trả kết quả gọi thử.
        if (dependencies.FirstOrDefault(d => d.Key == "database")?.Status != HealthStatus.Down)
        {
            var stuckRunBefore = now - AiRunStuckAfter;
            var stuckRuns = db.AiWorkflowRuns.AsNoTracking()
                .Where(r => (r.Status == AiRunStatus.Queued || r.Status == AiRunStatus.Running) && r.LastActivityAtUtc < stuckRunBefore);
            signals.Add(await CountAsync("aiRunsStuck", stuckRuns.Select(r => r.LastActivityAtUtc), _ => true, ct));

            var failedRuns = db.AiWorkflowRuns.AsNoTracking()
                .Where(r => (r.Status == AiRunStatus.Failed || r.Status == AiRunStatus.Interrupted) && r.LastActivityAtUtc >= dayAgo);
            signals.Add(await CountAsync("aiRunsFailed24h", failedRuns.Select(r => r.LastActivityAtUtc), _ => true, ct));

            // Hết hạn mức / AI bị tắt là chính sách, không phải lỗi hệ thống
            var failedCalls = db.AiUsageRecords.AsNoTracking()
                .Where(r => (r.Status == AiUsageStatus.Failed || r.Status == AiUsageStatus.ProviderUnavailable) && r.OccurredAtUtc >= dayAgo);
            signals.Add(await CountAsync("aiCallsFailed24h", failedCalls.Select(r => r.OccurredAtUtc), _ => true, ct));

            var failedEmails = db.EmailDeliveryLogs.AsNoTracking()
                .Where(l => !l.Succeeded && l.CreatedAt >= dayAgo);
            signals.Add(await CountAsync("emailsFailed24h", failedEmails.Select(l => l.CreatedAt), _ => true, ct));

            var stuckExportBefore = now - ExportStuckAfter;
            var stuckExports = db.ExportJobs.AsNoTracking()
                .Where(j => (j.Status == ExportJobStatus.Queued || j.Status == ExportJobStatus.Processing) && j.CreatedAt < stuckExportBefore);
            signals.Add(await CountAsync("exportsStuck", stuckExports.Select(j => j.CreatedAt), _ => true, ct));

            var failedExports = db.ExportJobs.AsNoTracking()
                .Where(j => j.Status == ExportJobStatus.Failed && (j.CompletedAt ?? j.CreatedAt) >= dayAgo);
            signals.Add(await CountAsync("exportsFailed24h", failedExports.Select(j => j.CreatedAt), _ => true, ct));

            var pendingTopUps = db.PaymentTransactions.AsNoTracking()
                .Where(t => t.Type == PaymentTransactionType.Topup && t.Status == PaymentTransactionStatus.Pending);
            signals.Add(await CountAsync("pendingTopUps", pendingTopUps.Select(t => t.CreatedAt),
                oldest => now - oldest > TopUpWaitWarning, ct));

            var pendingRequests = db.QuotaIncreaseRequests.AsNoTracking()
                .Where(r => r.Status == QuotaIncreaseRequestStatus.Pending);
            signals.Add(await CountAsync("pendingQuotaRequests", pendingRequests.Select(r => r.CreatedAt),
                oldest => now - oldest > QuotaRequestWaitWarning, ct));
        }

        var status = dependencies.Count == 0 ? HealthStatus.Healthy : dependencies.Max(d => d.Status);
        return Result<SystemHealthDto>.Success(new SystemHealthDto(status, now, ServerStartedAt, Version, dependencies, signals));
    }

    /// <param name="warnWhenOldest">Có ít nhất một việc thì gọi với mốc cũ nhất để quyết định có cảnh báo không.</param>
    private static async Task<OperationalSignalDto> CountAsync(
        string key, IQueryable<DateTime> timestamps, Func<DateTime, bool> warnWhenOldest, CancellationToken ct)
    {
        var count = await timestamps.CountAsync(ct);
        if (count == 0) return new OperationalSignalDto(key, 0, false, null);
        var oldest = await timestamps.MinAsync(ct);
        return new OperationalSignalDto(key, count, warnWhenOldest(oldest), oldest);
    }
}
