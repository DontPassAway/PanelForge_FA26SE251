using System.Net;
using System.Text.RegularExpressions;

namespace PanelForge.Application.Common.Email;

/// <summary>Một loại email hệ thống: mẫu mặc định và các biến <c>{{ten}}</c> được phép dùng.</summary>
public sealed record EmailTemplateDefinition(
    string Key,
    string DefaultSubject,
    string DefaultBodyHtml,
    IReadOnlyList<string> Placeholders,
    IReadOnlyList<string> RequiredPlaceholders);

/// <summary>
/// Danh mục mẫu email hệ thống. Admin sửa được tiêu đề và nội dung (PlatformEmailTemplate); chưa sửa thì dùng mẫu ở đây.
/// Biến trong nội dung được HTML-encode khi thay, nên tên người dùng hay tên Studio không chèn được HTML vào email.
/// </summary>
public static partial class EmailTemplateCatalog
{
    public const string EmailVerification = "EmailVerification";
    public const string PasswordReset = "PasswordReset";
    public const string ChangePasswordOtp = "ChangePasswordOtp";
    public const string StudioInviteNewAccount = "StudioInviteNewAccount";
    public const string StudioInviteExistingAccount = "StudioInviteExistingAccount";

    private const string Signature = "<p>Trân trọng,<br/>PanelForge Studio Team</p>";

    public static readonly IReadOnlyList<EmailTemplateDefinition> All =
    [
        new(EmailVerification,
            "[PanelForge] Xác nhận tài khoản email của bạn",
            """
            <h3>Xin chào {{name}},</h3>
            <p>Cảm ơn bạn đã đăng ký tài khoản tại PanelForge!</p>
            <p>Mã OTP xác thực email của bạn là: <strong><span style='font-size: 24px; color: #007bff;'>{{code}}</span></strong></p>
            <p>Mã này có hiệu lực trong vòng {{validFor}}. Vui lòng không chia sẻ mã này cho bất kỳ ai.</p>
            <br/>
            """ + Signature,
            ["name", "code", "validFor"],
            ["code"]),
        new(PasswordReset,
            "[PanelForge] Yêu cầu đặt lại mật khẩu",
            """
            <h3>Xin chào {{name}},</h3>
            <p>Chúng tôi nhận được yêu cầu đặt lại mật khẩu cho tài khoản của bạn tại PanelForge.</p>
            <p>Mã OTP đặt lại mật khẩu của bạn là: <strong><span style='font-size: 24px; color: #dc3545;'>{{code}}</span></strong></p>
            <p>Mã này có hiệu lực trong vòng {{validFor}}. Nếu bạn không yêu cầu điều này, xin vui lòng bỏ qua email.</p>
            <br/>
            """ + Signature,
            ["name", "code", "validFor"],
            ["code"]),
        new(ChangePasswordOtp,
            "[PanelForge] Mã xác thực OTP thay đổi mật khẩu",
            """
            <h3>Xin chào {{name}},</h3>
            <p>Chúng tôi nhận được yêu cầu thay đổi mật khẩu cho tài khoản PanelForge của bạn.</p>
            <p>Mã OTP xác thực thay đổi mật khẩu là: <strong><span style='font-size: 24px; color: #F97316;'>{{code}}</span></strong></p>
            <p>Mã này có hiệu lực trong vòng {{validFor}}. Tuyệt đối không chia sẻ mã này cho bất kỳ ai.</p>
            <br/>
            """ + Signature,
            ["name", "code", "validFor"],
            ["code"]),
        new(StudioInviteNewAccount,
            "[PanelForge] Bạn được mời tham gia Studio {{studio}}",
            """
            <h3>Xin chào {{name}},</h3>
            <p>Bạn được mời tham gia Studio <strong>{{studio}}</strong> trên PanelForge với vai trò <strong>{{role}}</strong>.</p>
            <p>Một tài khoản đã được tạo cho email này. Để kích hoạt, hãy mở trang <em>Đặt lại mật khẩu</em> và nhập mã OTP:</p>
            <p><strong><span style='font-size: 24px; color: #007bff;'>{{code}}</span></strong></p>
            <p>Mã có hiệu lực trong {{validFor}}. Nếu mã hết hạn, hãy dùng chức năng <em>Quên mật khẩu</em> với email này để nhận mã mới.</p>
            <p>Nếu bạn không mong đợi lời mời này, hãy bỏ qua email.</p>
            <br/>
            """ + Signature,
            ["name", "studio", "role", "code", "validFor"],
            ["code"]),
        new(StudioInviteExistingAccount,
            "[PanelForge] Bạn đã được thêm vào Studio {{studio}}",
            """
            <h3>Xin chào {{name}},</h3>
            <p>Bạn đã được thêm vào Studio <strong>{{studio}}</strong> trên PanelForge với vai trò <strong>{{role}}</strong>.</p>
            <p>Đăng nhập để xem Studio trong mục <em>Your Workspaces</em>.</p>
            <br/>
            """ + Signature,
            ["name", "studio", "role"],
            []),
    ];

    public static EmailTemplateDefinition? Find(string key) =>
        All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal));

    [GeneratedRegex(@"\{\{\s*([a-zA-Z]+)\s*\}\}")]
    private static partial Regex PlaceholderRegex();

    /// <summary>Tên biến xuất hiện trong văn bản (không trùng lặp).</summary>
    public static IReadOnlyList<string> PlaceholdersIn(string text) =>
        PlaceholderRegex().Matches(text).Select(m => m.Groups[1].Value).Distinct().ToList();

    /// <summary>Lý do mẫu không hợp lệ (biến lạ, thiếu biến bắt buộc), hoặc null nếu hợp lệ.</summary>
    public static string? Validate(EmailTemplateDefinition definition, string subject, string bodyHtml)
    {
        var unknown = PlaceholdersIn(subject + "\n" + bodyHtml).Except(definition.Placeholders).ToList();
        if (unknown.Count > 0)
            return $"Biến không hỗ trợ: {string.Join(", ", unknown.Select(u => "{{" + u + "}}"))}. "
                   + $"Biến dùng được: {string.Join(", ", definition.Placeholders.Select(p => "{{" + p + "}}"))}.";

        var missing = definition.RequiredPlaceholders.Except(PlaceholdersIn(bodyHtml)).ToList();
        if (missing.Count > 0)
            return $"Nội dung phải có {string.Join(", ", missing.Select(m => "{{" + m + "}}"))} để người nhận thấy mã.";

        return null;
    }

    /// <summary>Thay biến: nội dung HTML được encode giá trị; tiêu đề là văn bản thuần nên giữ nguyên.</summary>
    public static (string Subject, string BodyHtml) Render(
        string subject, string bodyHtml, IReadOnlyDictionary<string, string> values)
    {
        string Replace(string text, bool encode) => PlaceholderRegex().Replace(text, m =>
            values.TryGetValue(m.Groups[1].Value, out var v) ? (encode ? WebUtility.HtmlEncode(v) : v) : m.Value);

        return (Replace(subject, encode: false).ReplaceLineEndings(" "), Replace(bodyHtml, encode: true));
    }
}
