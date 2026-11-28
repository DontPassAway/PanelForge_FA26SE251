using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Quotas.Common;

/// <summary>Tên loại hạn mức trong nội dung thông báo (thông báo lưu sẵn văn bản, như các loại thông báo khác).</summary>
internal static class QuotaLabels
{
    public static string Of(QuotaResourceType type) => type switch
    {
        QuotaResourceType.Storage => "dung lượng lưu trữ",
        QuotaResourceType.AiTokens => "token AI mỗi tháng",
        QuotaResourceType.AiImages => "ảnh AI mỗi tháng",
        _ => type.ToString()
    };
}
