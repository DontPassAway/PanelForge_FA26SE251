namespace PanelForge.Application.Features.Payments.Models;

/// <summary>
/// Tài khoản nhận tiền VietQR khi Studio chưa khai "Cài đặt ngân hàng" (section <c>SePay:DefaultBank</c>,
/// env <c>SePay__DefaultBank__AccountNo</c>...). Phải trùng tài khoản đã liên kết SePay, nếu không webhook trả IgnoredAccountMismatch.
/// Giá trị mặc định là tài khoản giả chỉ dùng cho dev: production phải cấu hình tài khoản thật.
/// </summary>
public sealed class DefaultBankSettings
{
    public const string SectionName = "SePay:DefaultBank";
    public const string PlaceholderAccountNo = "0123456789";

    public string Bin { get; set; } = "970422";
    public string Name { get; set; } = "MB Bank";
    public string AccountNo { get; set; } = PlaceholderAccountNo;
    public string AccountName { get; set; } = "KOMAFORGE STUDIO";

    public bool IsPlaceholder => AccountNo.Trim() == PlaceholderAccountNo;
}
