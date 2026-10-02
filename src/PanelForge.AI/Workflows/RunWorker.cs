using PanelForge.AI.Contracts;
using Microsoft.Extensions.Options;

namespace PanelForge.AI.Workflows;

/// <summary>
/// BackgroundService chạy tuần tự từng run từ hàng đợi (free tier Gemini giới hạn tần suất → không chạy song song).
/// Mỗi run dùng scope DI riêng và token có timeout; không giữ dependency theo request HTTP đã kết thúc.
/// </summary>
public sealed class RunWorker : BackgroundService
{
    private readonly RunQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly WorkflowOptions _options;
    private readonly ILogger<RunWorker> _logger;

    public RunWorker(RunQueue queue, IServiceScopeFactory scopes, IOptions<WorkflowOptions> options, ILogger<RunWorker> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                await ProcessAsync(job, stoppingToken);
                _queue.Forget(job.RunId);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task ProcessAsync(AiRunJob job, CancellationToken stoppingToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.RunTimeoutSeconds));
        try
        {
            using var scope = _scopes.CreateScope();
            // job.ToString() đã ẩn ApiKey
            _logger.LogInformation("Bắt đầu run {Job}", job);
            await scope.ServiceProvider.GetRequiredService<MultiAgentWorkflow>().RunAsync(job, timeout.Token);
            _logger.LogInformation("Kết thúc run {RunId}", job.RunId);
        }
        catch (OperationCanceledException)
        {
            // Backend sẽ đánh dấu Interrupted nếu step cuối chưa kịp báo; không giả vờ thành công
            _logger.LogWarning("Run {RunId} bị huỷ/quá thời gian ({Timeout}s).", job.RunId, _options.RunTimeoutSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError("Run {RunId} lỗi: {Type} — {Message}", job.RunId, ex.GetType().Name, ex.Message);
        }
    }
}