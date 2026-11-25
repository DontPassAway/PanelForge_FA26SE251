using MediatR;
using PanelForge.Application.Features.Exports.Commands;

namespace PanelForge.API.Common;

/// <summary>
/// UC-14 / NFR-05: chạy các bản xuất chương đang chờ, lần lượt từng job. Cấu hình (tuỳ chọn): <c>Exports:Enabled</c> (true),
/// <c>Exports:PollSeconds</c> (5). Lỗi ở đây không ảnh hưởng request nghiệp vụ.
/// </summary>
public sealed class ExportWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<ExportWorker> _logger;

    public ExportWorker(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<ExportWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.GetValue("Exports:Enabled", true)) return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(_config.GetValue("Exports:PollSeconds", 5), 1, 300)));
        do
        {
            try
            {
                // Xử lý hết hàng đợi rồi mới chờ chu kỳ sau
                bool processed;
                do
                {
                    using var scope = _scopeFactory.CreateScope();
                    processed = await scope.ServiceProvider.GetRequiredService<IMediator>()
                        .Send(new ProcessNextExportJobCommand(), stoppingToken);
                }
                while (processed && !stoppingToken.IsCancellationRequested);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ExportWorker lỗi; sẽ thử lại ở chu kỳ sau.");
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
