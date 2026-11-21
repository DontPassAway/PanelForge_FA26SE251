using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Content;

/// <summary>
/// Vùng cấm chữ (keep-out zones) của một trang cho Letterer. Công cụ hỗ trợ bố cục, không phải nội dung truyện,
/// nên lưu bảng EF thường (không qua Marten). Ghi đè cả bộ mỗi lần PUT.
/// </summary>
public class PageKeepOutZones : BaseEntity
{
    public Guid PageId { get; private set; }
    /// <summary>JSON mảng {x,y,width,height,label,panelId}, toạ độ chuẩn hoá 0..1.</summary>
    public string ZonesJson { get; private set; } = "[]";
    public int Version { get; private set; }

    private PageKeepOutZones() { }

    public static PageKeepOutZones Create(Guid pageId) => new() { PageId = pageId };

    public void Replace(string zonesJson, string? userId)
    {
        ZonesJson = zonesJson;
        Version++;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = userId;
    }
}
