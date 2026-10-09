using MediatR;
using PanelForge.Application.Features.AiAgents.Commands.MarkStaleRuns;
using PanelForge.Application.Features.AiAgents.Models;

namespace PanelForge.API.Common;

/// <summary>
/// Định kỳ đánh dấu run AI Queued/Running không còn hoạt động là Interrupted (AI service chết / khởi động lại).
/// Chạy cả ngay khi Backend khởi động. Lỗi ở đây không ảnh hưởng request nghiệp vụ.
/// </summary>
public sealed class AiRunSweeper : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AiAgentSettings _settings;
    private readonly ILogger<AiRunSweeper> _logger;

    public AiRunSweeper(IServiceScopeFactory scopeFactory, AiAgentSettings settings, ILogger<AiRunSweeper> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(_settings.RunStaleAfterSeconds / 4, 5, 60));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var count = await scope.ServiceProvider.GetRequiredService<IMediator>()
                    .Send(new MarkStaleRunsCommand(), stoppingToken);
                if (count > 0) _logger.LogWarning("Đã đánh dấu {Count} run AI là Interrupted.", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AiRunSweeper lỗi; sẽ thử lại ở chu kỳ sau.");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}