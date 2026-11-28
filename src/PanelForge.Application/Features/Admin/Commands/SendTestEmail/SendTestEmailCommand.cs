using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Admin.Models;
using PanelForge.Domain.Entities.Platform;

namespace PanelForge.Application.Features.Admin.Commands.SendTestEmail;

/// <summary>
/// POST /api/admin/email-settings/test — gửi một email thử tới chính Admin bằng cấu hình SMTP đang dùng.
/// Thất bại trả EMAIL_DELIVERY_FAILED kèm ErrorDetails = <see cref="EmailDeliveryFailureDto"/> để Admin biết sửa gì.
/// </summary>
public sealed record SendTestEmailCommand(Guid AdminUserId) : IRequest<Result<SendTestEmailResponse>>;

public sealed class SendTestEmailCommandHandler : IRequestHandler<SendTestEmailCommand, Result<SendTestEmailResponse>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IEmailService _email;

    public SendTestEmailCommandHandler(IPanelForgeDbContext db, IEmailService email)
    {
        _db = db;
        _email = email;
    }

    public async Task<Result<SendTestEmailResponse>> Handle(SendTestEmailCommand command, CancellationToken ct)
    {
        var admin = await _db.Users.AsNoTracking()
            .Where(u => u.Id == command.AdminUserId)
            .Select(u => new { u.Email })
            .FirstOrDefaultAsync(ct);
        if (admin is null)
            return Result<SendTestEmailResponse>.Failure("Không tìm thấy tài khoản Admin.", ResultErrorCodes.NotFound);

        var sent = await _email.SendTestEmailAsync(admin.Email, ct);
        if (sent.Succeeded)
            return Result<SendTestEmailResponse>.Success(new SendTestEmailResponse(admin.Email));

        var reason = sent.FailureReason ?? EmailFailureReasons.SmtpError;
        return Result<SendTestEmailResponse>.Failure(
            MessageFor(reason),
            ResultErrorCodes.EmailDeliveryFailed,
            new EmailDeliveryFailureDto(reason, sent.Detail));
    }

    internal static string MessageFor(string reason) => reason switch
    {
        EmailFailureReasons.NotConfigured => "Chưa cấu hình email gửi đi và mật khẩu SMTP.",
        EmailFailureReasons.PasswordUnreadable => "Không đọc được mật khẩu SMTP đã lưu (khoá mã hoá của máy chủ đã đổi). Hãy nhập lại mật khẩu ứng dụng.",
        EmailFailureReasons.AuthFailed => "Máy chủ SMTP từ chối đăng nhập. Kiểm tra email gửi đi và mật khẩu ứng dụng (Gmail cần App Password, không dùng mật khẩu thường).",
        EmailFailureReasons.ConnectionFailed => "Không kết nối được máy chủ SMTP. Kiểm tra tên máy chủ và cổng (Gmail: smtp.gmail.com cổng 587).",
        EmailFailureReasons.TlsFailed => "Lỗi kết nối bảo mật TLS. Cổng này có thể không hỗ trợ STARTTLS; với Gmail dùng cổng 587.",
        EmailFailureReasons.Timeout => "Máy chủ SMTP không phản hồi kịp. Kiểm tra cổng và tường lửa của máy chủ.",
        EmailFailureReasons.SenderRejected => "Máy chủ SMTP từ chối địa chỉ gửi đi. Email gửi đi phải trùng tài khoản đăng nhập SMTP.",
        EmailFailureReasons.RecipientRejected => "Máy chủ SMTP từ chối địa chỉ nhận.",
        EmailFailureReasons.InvalidAddress => "Địa chỉ email gửi đi hoặc nhận không hợp lệ.",
        _ => "Không gửi được email thử. Xem chi tiết lỗi SMTP bên dưới."
    };
}
