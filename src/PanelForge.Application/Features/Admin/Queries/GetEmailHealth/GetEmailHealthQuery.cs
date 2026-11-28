using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;

namespace PanelForge.Application.Features.Admin.Queries.GetEmailHealth;

/// <summary>Trạng thái gửi email cho thẻ cảnh báo trên Tổng quan Admin.</summary>
public static class EmailHealthStatus
{
    /// <summary>Có cấu hình SMTP dùng được và lần gửi gần nhất (nếu có) thành công.</summary>
    public const string Ok = "OK";
    /// <summary>Chưa có cấu hình SMTP dùng được (thiếu, hoặc mật khẩu đã lưu không giải mã được).</summary>
    public const string NotConfigured = "NOT_CONFIGURED";
    /// <summary>Lần gửi gần nhất trong nhật ký bị lỗi.</summary>
    public const string LastSendFailed = "LAST_SEND_FAILED";
}

/// <param name="Problem">NOT_CONFIGURED | PASSWORD_UNREADABLE khi chưa gửi được (từ <c>EmailConfigurationStatus</c>).</param>
/// <param name="LastFailureReason">Mã trong <c>EmailFailureReasons</c> của lần gửi gần nhất nếu lỗi.</param>
public sealed record AdminEmailHealthDto(
    string Status,
    bool IsConfigured,
    string? Problem,
    DateTime? LastSentAt,
    bool? LastSucceeded,
    string? LastFailureReason,
    int RecentTotal,
    int RecentFailed,
    int WindowHours);

/// <summary>
/// GET /api/admin/email-health — chỉ đọc: cấu hình SMTP hiện tại (không kết nối máy chủ) + nhật ký gửi.
/// Dashboard Admin cảnh báo khi trạng thái khác OK. Hết cảnh báo khi đã cấu hình và lần gửi sau đó thành công.
/// </summary>
public sealed record GetEmailHealthQuery : IRequest<Result<AdminEmailHealthDto>>
{
    public const int WindowHours = 24;
}

public sealed class GetEmailHealthQueryHandler(IPanelForgeDbContext db, IEmailService email)
    : IRequestHandler<GetEmailHealthQuery, Result<AdminEmailHealthDto>>
{
    public async Task<Result<AdminEmailHealthDto>> Handle(GetEmailHealthQuery query, CancellationToken ct)
    {
        var config = await email.GetConfigurationStatusAsync(ct);

        var logs = db.EmailDeliveryLogs.AsNoTracking();
        var last = await logs
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new { l.CreatedAt, l.Succeeded, l.FailureReason })
            .FirstOrDefaultAsync(ct);

        var since = DateTime.UtcNow.AddHours(-GetEmailHealthQuery.WindowHours);
        var recentTotal = await logs.CountAsync(l => l.CreatedAt >= since, ct);
        var recentFailed = await logs.CountAsync(l => l.CreatedAt >= since && !l.Succeeded, ct);

        var status = !config.IsConfigured
            ? EmailHealthStatus.NotConfigured
            : last is { Succeeded: false }
                ? EmailHealthStatus.LastSendFailed
                : EmailHealthStatus.Ok;

        return Result<AdminEmailHealthDto>.Success(new AdminEmailHealthDto(
            status,
            config.IsConfigured,
            config.Problem,
            last?.CreatedAt,
            last?.Succeeded,
            last is { Succeeded: false } ? last.FailureReason : null,
            recentTotal,
            recentFailed,
            GetEmailHealthQuery.WindowHours));
    }
}
