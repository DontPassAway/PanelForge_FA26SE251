using PanelForge.Application.Common.Email;

namespace PanelForge.Application.Features.Admin.Models;

/// <summary>
/// Mẫu một loại email hệ thống cho màn Admin. <c>IsCustomized = false</c>: đang dùng nội dung mặc định.
/// <c>HasCode</c>: email có ô mã OTP do hệ thống tự đặt sau đoạn mở đầu.
/// </summary>
public sealed record AdminEmailTemplateDto(
    string Key,
    EmailTemplateContent Content,
    EmailTemplateContent DefaultContent,
    IReadOnlyList<string> Placeholders,
    bool HasCode,
    bool IsCustomized,
    DateTime? ChangedAt,
    string? ChangedByName);

/// <summary>Email đã dựng với dữ liệu mẫu, đúng như khi gửi.</summary>
public sealed record EmailTemplatePreviewDto(string Subject, string BodyHtml);
