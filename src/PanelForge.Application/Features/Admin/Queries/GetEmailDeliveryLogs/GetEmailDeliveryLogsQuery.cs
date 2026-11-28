using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Admin.Models;

namespace PanelForge.Application.Features.Admin.Queries.GetEmailDeliveryLogs;

/// <summary>
/// GET /api/admin/email-logs — nhật ký gửi email, mới nhất trước.
/// </summary>
/// <param name="Succeeded">true = chỉ thư gửi được, false = chỉ thư lỗi, null = tất cả.</param>
/// <param name="MailType">Mã trong EmailMailTypes.</param>
/// <param name="Search">Tìm gần đúng theo email người nhận.</param>
/// <param name="From">Từ thời điểm (UTC, tính cả mốc này).</param>
/// <param name="To">Đến thời điểm (UTC, tính cả mốc này).</param>
public sealed record GetEmailDeliveryLogsQuery(
    bool? Succeeded = null,
    string? MailType = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 30,
    DateTime? From = null,
    DateTime? To = null) : IRequest<Result<EmailDeliveryLogsPageDto>>;

public sealed class GetEmailDeliveryLogsQueryHandler(IPanelForgeDbContext db)
    : IRequestHandler<GetEmailDeliveryLogsQuery, Result<EmailDeliveryLogsPageDto>>
{
    public async Task<Result<EmailDeliveryLogsPageDto>> Handle(GetEmailDeliveryLogsQuery q, CancellationToken ct)
    {
        var page = Math.Max(1, q.Page);
        var pageSize = Math.Clamp(q.PageSize, 1, 100);

        if (q.From is { } f && q.To is { } t && f > t)
            return Result<EmailDeliveryLogsPageDto>.Failure("Khoảng thời gian không hợp lệ: 'from' phải trước 'to'.", ResultErrorCodes.Validation);

        var query = db.EmailDeliveryLogs.AsNoTracking();
        if (q.From is { } from) query = query.Where(l => l.CreatedAt >= from);
        if (q.To is { } to) query = query.Where(l => l.CreatedAt <= to);
        if (q.Succeeded is { } succeeded) query = query.Where(l => l.Succeeded == succeeded);
        if (!string.IsNullOrWhiteSpace(q.MailType)) query = query.Where(l => l.MailType == q.MailType);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = q.Search.Trim().ToLower();
            query = query.Where(l => l.ToEmail.ToLower().Contains(term));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new EmailDeliveryLogDto(
                l.Id, l.CreatedAt, l.ToEmail, l.MailType, l.Subject, l.Succeeded,
                l.FailureReason, l.ErrorDetail, l.SmtpServer, l.SmtpPort))
            .ToListAsync(ct);

        var dayAgo = DateTime.UtcNow.AddDays(-1);
        var failed24h = await db.EmailDeliveryLogs.AsNoTracking()
            .CountAsync(l => !l.Succeeded && l.CreatedAt >= dayAgo, ct);

        return Result<EmailDeliveryLogsPageDto>.Success(new EmailDeliveryLogsPageDto(items, total, page, pageSize, failed24h));
    }
}
