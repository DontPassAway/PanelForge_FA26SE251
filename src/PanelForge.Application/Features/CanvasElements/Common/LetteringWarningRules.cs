using PanelForge.AI.Contracts;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.CanvasElements.Common;

/// <param name="Acknowledged">Chỉ dùng cho TEXT_OVERFLOW: Letterer đã xác nhận kèm lý do (BR-20), không còn chặn cổng Lettering.</param>
public sealed record ElementWarningDto(
    Guid ElementId, string Code, string Message, Guid? RelatedElementId = null,
    bool Acknowledged = false, string? AcknowledgedReason = null);

/// <summary>
/// BR-20 / FR-Letterer-03: cảnh báo do CODE (không phải mô hình), chỉ cảnh báo, không chặn nộp.
/// Tọa độ element là px trên canvas Page. Key Artwork Region là element loại <see cref="KeyRegionType"/> do Artist đặt.
/// </summary>
public static class LetteringWarningRules
{
    public const string TextOverflow = "TEXT_OVERFLOW";
    public const string ReadingOrderMismatch = "READING_ORDER_MISMATCH";
    public const string OverlapsKeyRegion = "OVERLAPS_KEY_ARTWORK_REGION";
    public const string KeyRegionType = "KEY_ARTWORK_REGION";

    /// <param name="active">Element chưa bị xóa của Page.</param>
    /// <param name="lineOrder">ScriptLineId → (số Scene, thứ tự dòng); thiếu = dòng đã xóa, bỏ qua kiểm thứ tự.</param>
    public static List<ElementWarningDto> Evaluate(
        IReadOnlyCollection<ElementState> active,
        ReadingDirection direction,
        IReadOnlyDictionary<Guid, (int Scene, int Line)> lineOrder)
    {
        var warnings = new List<ElementWarningDto>();
        var texts = active.Where(e => Page.TextElementTypes.Contains(e.ElementType)).ToList();
        var regions = active.Where(e => e.ElementType == KeyRegionType).ToList();

        foreach (var e in texts)
        {
            var text = (e.RenderedText ?? e.Content)?.Trim();
            var limit = LetteringLimits.ForElement(e.Width, e.Height);
            if (!string.IsNullOrEmpty(text) && text.Length > limit)
                warnings.Add(new(e.ElementId, TextOverflow,
                    $"Chữ dài {text.Length} ký tự, ước lượng hộp chỉ chứa khoảng {limit}. Hãy phóng to hộp hoặc rút gọn thoại.",
                    Acknowledged: e.OverflowAckReason is not null, AcknowledgedReason: e.OverflowAckReason));

            foreach (var r in regions.Where(r => Intersects(e, r)))
                warnings.Add(new(e.ElementId, OverlapsKeyRegion, "Đè lên Key Artwork Region do Artist đánh dấu.", r.ElementId));
        }

        var bound = texts
            .Where(e => e.ScriptLineId is { } id && lineOrder.ContainsKey(id))
            .OrderBy(e => lineOrder[e.ScriptLineId!.Value])
            .ToList();
        for (var i = 1; i < bound.Count; i++)
            if (Precedes(bound[i], bound[i - 1], direction))
                warnings.Add(new(bound[i].ElementId, ReadingOrderMismatch,
                    "Thoại này đứng sau trong script nhưng nằm trước thoại kia theo thứ tự đọc của Series.", bound[i - 1].ElementId));

        return warnings;
    }

    private static bool Intersects(ElementState a, ElementState b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    /// <summary>a nằm trước b theo thứ tự đọc: sắp theo hàng từ trên xuống, trong hàng theo hướng đọc.</summary>
    private static bool Precedes(ElementState a, ElementState b, ReadingDirection direction)
    {
        // Letterer đã ghi đè thứ tự cho cả hai thì dùng số thứ tự đó thay vì suy từ vị trí
        if (a.ReadingOrder is { } ao && b.ReadingOrder is { } bo && ao != bo) return ao < bo;

        var rowTolerance = Math.Min(a.Height, b.Height) / 2;
        if (Math.Abs(a.Y - b.Y) > rowTolerance) return a.Y < b.Y;
        var ax = a.X + a.Width / 2;
        var bx = b.X + b.Width / 2;
        return direction == ReadingDirection.RightToLeft ? ax > bx : ax < bx;
    }
}
