using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Platform;

/// <summary>
/// Mẫu nội dung một loại email hệ thống (OTP xác thực, đặt lại mật khẩu, lời mời Studio...) do Admin sửa trong app.
/// Append-only: mỗi lần lưu thêm một bản ghi; bản mới nhất của từng <see cref="TemplateKey"/> là mẫu đang dùng.
/// Chưa có bản ghi nào thì dùng mẫu mặc định trong code.
/// </summary>
public class PlatformEmailTemplate : BaseEntity
{
    public const int MaxSubjectLength = 200;
    public const int MaxBodyLength = 20000;

    public string TemplateKey { get; private set; } = default!;
    public string Subject { get; private set; } = default!;
    public string BodyHtml { get; private set; } = default!;
    public Guid ChangedByUserId { get; private set; }

    private PlatformEmailTemplate() { }

    public static PlatformEmailTemplate Create(string templateKey, string subject, string bodyHtml, Guid changedByUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);
        if (string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("Nhập tiêu đề email.", nameof(subject));
        if (string.IsNullOrWhiteSpace(bodyHtml))
            throw new ArgumentException("Nhập nội dung email.", nameof(bodyHtml));

        var trimmedSubject = subject.Trim();
        if (trimmedSubject.Length > MaxSubjectLength || trimmedSubject.Contains('\n') || trimmedSubject.Contains('\r'))
            throw new ArgumentException($"Tiêu đề tối đa {MaxSubjectLength} ký tự và nằm trên một dòng.", nameof(subject));
        if (bodyHtml.Length > MaxBodyLength)
            throw new ArgumentException($"Nội dung email tối đa {MaxBodyLength} ký tự.", nameof(bodyHtml));

        return new PlatformEmailTemplate
        {
            TemplateKey = templateKey,
            Subject = trimmedSubject,
            BodyHtml = bodyHtml.Trim(),
            ChangedByUserId = changedByUserId
        };
    }
}
