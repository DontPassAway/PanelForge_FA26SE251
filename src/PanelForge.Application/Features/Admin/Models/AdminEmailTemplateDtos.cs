namespace PanelForge.Application.Features.Admin.Models;

/// <summary>
/// Mẫu một loại email hệ thống cho màn Admin. <c>IsCustomized = false</c>: đang dùng mẫu mặc định.
/// Biến viết dạng <c>{{ten}}</c>; <c>RequiredPlaceholders</c> phải có trong nội dung (ví dụ <c>{{code}}</c> của email OTP).
/// </summary>
public sealed record AdminEmailTemplateDto(
    string Key,
    string Subject,
    string BodyHtml,
    string DefaultSubject,
    string DefaultBodyHtml,
    IReadOnlyList<string> Placeholders,
    IReadOnlyList<string> RequiredPlaceholders,
    bool IsCustomized,
    DateTime? ChangedAt,
    string? ChangedByName);

public sealed record UpdateEmailTemplateRequest(string Subject, string BodyHtml);
