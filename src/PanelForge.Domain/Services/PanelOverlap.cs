using System.Text.Json;

namespace PanelForge.Domain.Services;

public static class PanelOverlap
{
    public const double MinimumOverlapRatio = 0.15;

    public static IReadOnlyList<Guid> FindIntersectingPanelIds(
        string regionJson,
        IEnumerable<(Guid PanelId, string BoundingBox)> panels)
    {
        if (!TryParseRegion(regionJson, out var error)) return [];

        return panels
            .Select(panel => (panel.PanelId, Box: ParsePanelBox(panel.BoundingBox)))
            .Where(panel => panel.Box is not null && OverlapRatio(error, panel.Box.Value) >= MinimumOverlapRatio)
            .Select(panel => panel.PanelId)
            .ToList();
    }

    private static double OverlapRatio(Box error, Box panel)
    {
        var width = Math.Max(0, Math.Min(error.X + error.Width, panel.X + panel.Width) - Math.Max(error.X, panel.X));
        var height = Math.Max(0, Math.Min(error.Y + error.Height, panel.Y + panel.Height) - Math.Max(error.Y, panel.Y));
        // D8: mẫu số là diện tích nhỏ hơn giữa vùng lỗi và Panel, để vùng lỗi lớn phủ trọn Panel nhỏ vẫn đạt ngưỡng.
        // Box có diện tích 0 không giao với gì (tránh chia cho 0).
        var minArea = Math.Min(error.Width * error.Height, panel.Width * panel.Height);
        if (minArea <= 0) return 0;
        return width * height / minArea;
    }

    private static bool TryParseRegion(string json, out Box box)
    {
        box = default;
        try
        {
            using var document = JsonDocument.Parse(json);
            return TryReadBox(document.RootElement, normalized: true, out box);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static Box? ParsePanelBox(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("normalized", out var normalized) &&
                TryReadBox(normalized, normalized: true, out var nested))
                return nested;

            if (!TryReadBox(root, normalized: false, out var raw)) return null;

            var coordinateSpace = ReadString(root, "coordinateSpace");
            var pageWidth = ReadNumber(root, "pageWidthPx") ?? 1200;
            var pageHeight = ReadNumber(root, "pageHeightPx") ?? 1800;
            if (coordinateSpace == "page-normalized" ||
                raw.X <= 1 && raw.Y <= 1 && raw.Width <= 1 && raw.Height <= 1)
                return raw;

            if (ReadString(root, "unit") == "px" || raw.Width > 100 || raw.Height > 100 ||
                root.TryGetProperty("pageWidthPx", out _))
                return new Box(raw.X / pageWidth, raw.Y / pageHeight, raw.Width / pageWidth, raw.Height / pageHeight);

            return new Box(raw.X / 100, raw.Y / 100, raw.Width / 100, raw.Height / 100);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryReadBox(JsonElement element, bool normalized, out Box box)
    {
        box = default;
        var x = ReadNumber(element, "x");
        var y = ReadNumber(element, "y");
        var width = ReadNumber(element, "w") ?? ReadNumber(element, "width");
        var height = ReadNumber(element, "h") ?? ReadNumber(element, "height");
        if (x is null || y is null || width is null || height is null || width <= 0 || height <= 0) return false;

        var divisor = normalized && (x > 1 || y > 1 || width > 1 || height > 1) ? 100 : 1;
        box = new Box(x.Value / divisor, y.Value / divisor, width.Value / divisor, height.Value / divisor);
        return true;
    }

    private static double? ReadNumber(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private readonly record struct Box(double X, double Y, double Width, double Height);
}
