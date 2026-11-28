using System.Text.RegularExpressions;

namespace PanelForge.Application.Common.Email;

/// <summary>Phần chữ Admin sửa được của một email. Chữ thường, xuống dòng bằng Enter, chèn biến dạng <c>{{name}}</c>.</summary>
public sealed record EmailTemplateContent(string Subject, string Greeting, string Intro, string Note, string Signature);

/// <summary>
/// Một loại email hệ thống. <c>HasCode</c>: email có ô mã OTP, hệ thống tự đặt sau đoạn mở đầu.
/// <c>Placeholders</c>: biến Admin chèn được vào chữ.
/// </summary>
public sealed record EmailTemplateDefinition(
    string Key,
    EmailTemplateContent Default,
    IReadOnlyList<string> Placeholders,
    bool HasCode,
    string CodeColor);

/// <summary>
/// Danh mục email hệ thống và cách dựng email. Admin chỉ sửa chữ (PlatformEmailTemplate); bố cục và ô mã OTP cố định ở đây,
/// nên email luôn có mã và Admin không cần biết HTML. Chữ và giá trị biến đều được HTML-encode khi dựng.
/// </summary>
public static partial class EmailTemplateCatalog
{
    public const string EmailVerification = "EmailVerification";
    public const string PasswordReset = "PasswordReset";
    public const string ChangePasswordOtp = "ChangePasswordOtp";
    public const string StudioInviteNewAccount = "StudioInviteNewAccount";
    public const string StudioInviteExistingAccount = "StudioInviteExistingAccount";

    private const string DefaultGreeting = "Xin chào {{name}},";
    private const string DefaultSignature = "Trân trọng,\nPanelForge Studio Team";

    public static readonly IReadOnlyList<EmailTemplateDefinition> All =
    [
        new(EmailVerification,
            new("[PanelForge] Xác nhận tài khoản email của bạn", DefaultGreeting,
                "Cảm ơn bạn đã đăng ký tài khoản tại PanelForge! Mã OTP xác thực email của bạn là:",
                "Mã này có hiệu lực trong vòng {{validFor}}. Vui lòng không chia sẻ mã này cho bất kỳ ai.",
                DefaultSignature),
            ["name", "validFor"], HasCode: true, CodeColor: "#2563EB"),
        new(PasswordReset,
            new("[PanelForge] Yêu cầu đặt lại mật khẩu", DefaultGreeting,
                "Chúng tôi nhận được yêu cầu đặt lại mật khẩu cho tài khoản của bạn tại PanelForge. Mã OTP đặt lại mật khẩu của bạn là:",
                "Mã này có hiệu lực trong vòng {{validFor}}. Nếu bạn không yêu cầu điều này, xin vui lòng bỏ qua email.",
                DefaultSignature),
            ["name", "validFor"], HasCode: true, CodeColor: "#DC2626"),
        new(ChangePasswordOtp,
            new("[PanelForge] Mã xác thực OTP thay đổi mật khẩu", DefaultGreeting,
                "Chúng tôi nhận được yêu cầu thay đổi mật khẩu cho tài khoản PanelForge của bạn. Mã OTP xác thực thay đổi mật khẩu là:",
                "Mã này có hiệu lực trong vòng {{validFor}}. Tuyệt đối không chia sẻ mã này cho bất kỳ ai.",
                DefaultSignature),
            ["name", "validFor"], HasCode: true, CodeColor: "#EA580C"),
        new(StudioInviteNewAccount,
            new("[PanelForge] Bạn được mời tham gia Studio {{studio}}", DefaultGreeting,
                "Bạn được mời tham gia Studio {{studio}} trên PanelForge với vai trò {{role}}.\n"
                + "Một tài khoản đã được tạo cho email này. Để kích hoạt, hãy mở trang Đặt lại mật khẩu và nhập mã OTP:",
                "Mã có hiệu lực trong {{validFor}}. Nếu mã hết hạn, hãy dùng chức năng Quên mật khẩu với email này để nhận mã mới.\n"
                + "Nếu bạn không mong đợi lời mời này, hãy bỏ qua email.",
                DefaultSignature),
            ["name", "studio", "role", "validFor"], HasCode: true, CodeColor: "#2563EB"),
        new(StudioInviteExistingAccount,
            new("[PanelForge] Bạn đã được thêm vào Studio {{studio}}", DefaultGreeting,
                "Bạn đã được thêm vào Studio {{studio}} trên PanelForge với vai trò {{role}}.",
                "Đăng nhập để xem Studio trong mục Your Workspaces.",
                DefaultSignature),
            ["name", "studio", "role"], HasCode: false, CodeColor: "#2563EB"),
    ];

    /// <summary>Giá trị mẫu cho xem trước và email thử.</summary>
    public static IReadOnlyDictionary<string, string> SampleValues(string recipientName) => new Dictionary<string, string>
    {
        ["name"] = recipientName,
        ["code"] = "482913",
        ["validFor"] = "15 phút",
        ["studio"] = "Studio Mực Cam",
        ["role"] = "Artist"
    };

    public static EmailTemplateDefinition? Find(string key) =>
        All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal));

    [GeneratedRegex(@"\{\{\s*([a-zA-Z]+)\s*\}\}")]
    private static partial Regex PlaceholderRegex();

    private static IEnumerable<string> PlaceholdersIn(EmailTemplateContent c) =>
        PlaceholderRegex().Matches(string.Join('\n', c.Subject, c.Greeting, c.Intro, c.Note, c.Signature))
            .Select(m => m.Groups[1].Value).Distinct();

    /// <summary>Lý do nội dung không hợp lệ (dùng biến không có), hoặc null nếu hợp lệ.</summary>
    public static string? Validate(EmailTemplateDefinition definition, EmailTemplateContent content)
    {
        var unknown = PlaceholdersIn(content).Except(definition.Placeholders).ToList();
        return unknown.Count == 0
            ? null
            : $"Thông tin không dùng được trong email này: {string.Join(", ", unknown.Select(u => "{{" + u + "}}"))}.";
    }

    /// <summary>Chỉ escape ký tự đặc biệt của HTML; giữ nguyên chữ tiếng Việt để email dễ đọc khi xem mã nguồn.</summary>
    private static string Encode(string text) => text
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");

    /// <summary>Dựng tiêu đề (văn bản thuần) và email HTML theo bố cục cố định; mọi chữ đều được encode.</summary>
    public static (string Subject, string BodyHtml) Render(
        EmailTemplateDefinition definition, EmailTemplateContent content, IReadOnlyDictionary<string, string> values)
    {
        string Fill(string text) => PlaceholderRegex().Replace(text, m =>
            values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
        string Html(string text) =>
            Encode(Fill(text)).ReplaceLineEndings("\n").Replace("\n", "<br/>");

        var body = new System.Text.StringBuilder()
            .Append("<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:1.6;color:#1C1917;max-width:560px\">")
            .Append("<p style=\"font-size:17px;font-weight:bold;margin:0 0 12px\">").Append(Html(content.Greeting)).Append("</p>")
            .Append("<p style=\"margin:0 0 12px\">").Append(Html(content.Intro)).Append("</p>");

        if (definition.HasCode && values.TryGetValue("code", out var code))
            body.Append("<p style=\"margin:16px 0\"><span style=\"display:inline-block;padding:10px 18px;border-radius:8px;")
                .Append("background:#FAF9F5;border:1px solid #E7E5E4;font-size:26px;font-weight:bold;letter-spacing:4px;color:")
                .Append(definition.CodeColor).Append("\">").Append(Encode(code)).Append("</span></p>");

        if (!string.IsNullOrWhiteSpace(content.Note))
            body.Append("<p style=\"margin:0 0 12px\">").Append(Html(content.Note)).Append("</p>");
        if (!string.IsNullOrWhiteSpace(content.Signature))
            body.Append("<p style=\"margin:20px 0 0\">").Append(Html(content.Signature)).Append("</p>");

        return (Fill(content.Subject).ReplaceLineEndings(" "), body.Append("</div>").ToString());
    }
}
