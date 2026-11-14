using System.Text;
using System.Text.RegularExpressions;

namespace PanelForge.Domain.Common;

/// <summary>
/// Chuẩn hoá tên thực thể và bí danh cho Series Bible (BIB-09, Q16, 9A):
/// trim -> gộp khoảng trắng liên tiếp -> Unicode NFC -> ToLowerInvariant().
/// Dùng chung cho bảng chỉ mục tên (bible_entry_names) và SpeakerResolver (BIB-12).
/// </summary>
public static class BibleNameNormalizer
{
    private static readonly Regex MultipleSpacesRegex = new(@"\s+", RegexOptions.Compiled);

    public static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return string.Empty;

        var trimmed = s.Trim();
        var collapsed = MultipleSpacesRegex.Replace(trimmed, " ");
        var nfc = collapsed.Normalize(NormalizationForm.FormC);
        return nfc.ToLowerInvariant();
    }
}
