using MediatR;
using PanelForge.Application.Features.Notifications.Commands.SendAssignmentReminders;

namespace PanelForge.API.Common;

/// <summary>
/// Định kỳ nhắc người nhận việc sắp đến hạn hoặc đã quá hạn (UC-14). Chạy cả ngay khi Backend khởi động.
/// Cấu hình (đều tuỳ chọn): <c>Reminders:Enabled</c> (true), <c>Reminders:IntervalMinutes</c> (15), <c>Reminders:LeadHours</c> (24).
/// Lỗi ở đây không ảnh hưởng request nghiệp vụ.
/// </summary>
public sealed class AssignmentReminderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<AssignmentReminderWorker> _logger;

    public AssignmentReminderWorker(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<AssignmentReminderWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.GetValue("Reminders:Enabled", true)) return;

        var interval = TimeSpan.FromMinutes(Math.Clamp(_config.GetValue("Reminders:IntervalMinutes", 15), 1, 1440));
        var lead = TimeSpan.FromHours(Math.Clamp(_config.GetValue("Reminders:LeadHours", 24), 1, 24 * 14));

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var count = await scope.ServiceProvider.GetRequiredService<IMediator>()
                    .Send(new SendAssignmentRemindersCommand(lead), stoppingToken);
                if (count > 0) _logger.LogInformation("Đã gửi {Count} thông báo nhắc hạn.", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AssignmentReminderWorker lỗi; sẽ thử lại ở chu kỳ sau.");
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
