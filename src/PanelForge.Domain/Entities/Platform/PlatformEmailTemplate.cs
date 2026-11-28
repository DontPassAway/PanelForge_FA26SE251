using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Platform;

/// <summary>
/// Nội dung một loại email hệ thống (OTP xác thực, đặt lại mật khẩu, lời mời Studio...) do Admin sửa trong app.
/// Admin chỉ nhập chữ thường theo từng phần (lời chào, mở đầu, lưu ý, chữ ký); bố cục email và ô mã OTP do hệ thống dựng,
/// nên Admin không cần biết HTML và không làm mất mã được.
/// Append-only: mỗi lần lưu thêm một bản ghi; bản mới nhất của từng <see cref="TemplateKey"/> là mẫu đang dùng.
/// </summary>
public class PlatformEmailTemplate : BaseEntity
{
    public const int MaxSubjectLength = 200;
    public const int MaxFieldLength = 2000;

    public string TemplateKey { get; private set; } = default!;
    public string Subject { get; private set; } = default!;
    public string Greeting { get; private set; } = default!;
    public string Intro { get; private set; } = default!;
    public string Note { get; private set; } = default!;
    public string Signature { get; private set; } = default!;
    public Guid ChangedByUserId { get; private set; }

    private PlatformEmailTemplate() { }

    public static PlatformEmailTemplate Create(
        string templateKey, string subject, string greeting, string intro, string? note, string? signature, Guid changedByUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);

        var trimmedSubject = (subject ?? string.Empty).Trim();
        if (trimmedSubject.Length == 0 || trimmedSubject.Length > MaxSubjectLength
            || trimmedSubject.Contains('\n') || trimmedSubject.Contains('\r'))
            throw new ArgumentException($"Tiêu đề bắt buộc, tối đa {MaxSubjectLength} ký tự và nằm trên một dòng.", nameof(subject));
        if (string.IsNullOrWhiteSpace(greeting))
            throw new ArgumentException("Nhập lời chào.", nameof(greeting));
        if (string.IsNullOrWhiteSpace(intro))
            throw new ArgumentException("Nhập đoạn mở đầu.", nameof(intro));

        string Field(string? value, string name)
        {
            var text = (value ?? string.Empty).Trim().ReplaceLineEndings("\n");
            if (text.Length > MaxFieldLength)
                throw new ArgumentException($"Mỗi phần nội dung tối đa {MaxFieldLength} ký tự.", name);
            return text;
        }

        return new PlatformEmailTemplate
        {
            TemplateKey = templateKey,
            Subject = trimmedSubject,
            Greeting = Field(greeting, nameof(greeting)),
            Intro = Field(intro, nameof(intro)),
            Note = Field(note, nameof(note)),
            Signature = Field(signature, nameof(signature)),
            ChangedByUserId = changedByUserId
        };
    }
}
