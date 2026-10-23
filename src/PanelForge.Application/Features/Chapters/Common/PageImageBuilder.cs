using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Application.Features.Chapters.Common;

/// <summary>
/// Dựng ảnh hoàn chỉnh của một trang từ các artwork của nó (theo <see cref="FinalArtwork"/> hoặc snapshot đã lưu):
/// ảnh cả trang dùng nguyên; ảnh Panel được ghép vào đúng khung; sau đó vẽ lớp chữ của trang lên trên.
/// Dùng chung cho trình đọc công khai, xem thử và xuất file.
/// </summary>
internal static class PageImageBuilder
{
    /// <summary>Trả null nếu trang không có artwork nào trong <paramref name="assetReferenceIds"/> đọc được từ kho.</summary>
    public static async Task<ExportPageImage?> BuildAsync(
        IPanelForgeDbContext db, IAssetStorage storage, IPageRepository pageRepository, IPageLetteringCompositor compositor,
        Guid pageId, IReadOnlyCollection<Guid> assetReferenceIds, CancellationToken ct)
    {
        // Snapshot có thể trỏ tới trang/Panel đã xoá sau khi duyệt: bỏ bộ lọc xoá mềm để dựng lại đúng bản đã duyệt
        var page = await db.Pages.IgnoreQueryFilters().AsNoTracking().Where(p => p.Id == pageId)
            .Select(p => new { p.PageNumber, p.WidthPx, p.HeightPx }).FirstOrDefaultAsync(ct);
        if (page is null) return null;
        int width = page.WidthPx > 0 ? page.WidthPx : 1200, height = page.HeightPx > 0 ? page.HeightPx : 1800;

        var refs = await (
            from r in db.AssetReferences.AsNoTracking()
            where assetReferenceIds.Contains(r.Id) && r.PageId == pageId
            join a in db.Assets.AsNoTracking() on r.AssetId equals a.Id
            select new { r.PanelId, a.StorageKey, a.ContentType }).ToListAsync(ct);
        if (refs.Count == 0) return null;

        ExportPageImage image;
        var whole = refs.FirstOrDefault(r => r.PanelId == null);
        if (whole is not null)
        {
            var bytes = await ReadAsync(storage, whole.StorageKey, ct);
            if (bytes is null) return null;
            image = new ExportPageImage(page.PageNumber, bytes, whole.ContentType);
        }
        else
        {
            var panelIds = refs.Select(r => r.PanelId!.Value).ToList();
            var panels = await db.Panels.IgnoreQueryFilters().AsNoTracking().Where(p => panelIds.Contains(p.Id))
                .OrderBy(p => p.PanelNumber).Select(p => new { p.Id, p.BoundingBox }).ToListAsync(ct);
            var layers = new List<PagePanelLayer>();
            foreach (var panel in panels)
            {
                var source = refs.First(r => r.PanelId == panel.Id);
                var box = PanelBox.Parse(panel.BoundingBox, width, height);
                layers.Add(new PagePanelLayer(await ReadAsync(storage, source.StorageKey, ct),
                    box.X, box.Y, box.Width, box.Height, box.Corners, box.Borderless));
            }
            image = compositor.ComposePanels(page.PageNumber, width, height, layers);
        }

        return await WithLetteringAsync(pageRepository, compositor, image, pageId, ct);
    }

    /// <summary>
    /// Vẽ lớp chữ của Page lên ảnh trang (bóng thoại, dẫn truyện, SFX). Chương đã Approved/Published thì Page chỉ đọc
    /// (ChapterEditGuard), nên trạng thái hiện tại của stream chính là lớp chữ đã duyệt.
    /// </summary>
    private static async Task<ExportPageImage> WithLetteringAsync(
        IPageRepository pageRepository, IPageLetteringCompositor compositor, ExportPageImage image, Guid pageId, CancellationToken ct)
    {
        var page = await pageRepository.GetAsync(pageId, ct);
        if (page is null) return image;
        var texts = page.Elements.Values
            .Where(e => !e.IsRemoved && Page.TextElementTypes.Contains(e.ElementType))
            .Select(e => new ExportLetteringElement(e.ElementType, e.X, e.Y, e.Width, e.Height, e.Rotation, e.ZIndex,
                e.RenderedText ?? e.Content ?? "", e.Style))
            .ToList();
        return compositor.Compose(image, page.WidthPx, page.HeightPx, texts);
    }

    private static async Task<byte[]?> ReadAsync(IAssetStorage storage, string key, CancellationToken ct)
    {
        await using var stream = await storage.OpenReadAsync(key, ct);
        if (stream is null) return null;
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }
}

/// <summary>
/// Hộp khung px trên trang từ JSON <c>Panel.BoundingBox</c> — cùng quy tắc FE <c>parseBoundingBox</c>:
/// ưu tiên x/y/width/height px; có <c>normalized</c> (0–1 hoặc %) thì dùng khi thiếu px; toàn bộ ≤ 1 coi là chuẩn hoá 0–1.
/// </summary>
internal sealed record PanelBox(double X, double Y, double Width, double Height,
    IReadOnlyList<(double X, double Y)>? Corners, bool Borderless)
{
    public static PanelBox Parse(string? json, int pageWidth, int pageHeight)
    {
        var fallback = new PanelBox(0.1 * pageWidth, 0.1 * pageHeight, 0.8 * pageWidth, 0.35 * pageHeight, null, false);
        if (string.IsNullOrWhiteSpace(json)) return fallback;
        try
        {
            var root = JsonDocument.Parse(json).RootElement;
            var corners = root.TryGetProperty("corners", out var c) && c.ValueKind == JsonValueKind.Array
                ? c.EnumerateArray().Select(p => (Num(p, "x") ?? 0, Num(p, "y") ?? 0)).ToList()
                : null;
            var borderless = root.TryGetProperty("borderless", out var b) && b.ValueKind == JsonValueKind.True;
            double pw = Num(root, "pageWidthPx") ?? pageWidth, ph = Num(root, "pageHeightPx") ?? pageHeight;

            // Toạ độ theo khổ trang lúc vẽ (pageWidthPx) → đổi sang khổ trang hiện tại
            PanelBox Scale(PanelBox box) => pw == pageWidth && ph == pageHeight
                ? box
                : box with
                {
                    X = box.X * pageWidth / pw, Y = box.Y * pageHeight / ph,
                    Width = box.Width * pageWidth / pw, Height = box.Height * pageHeight / ph
                };

            if (root.TryGetProperty("normalized", out var n) && n.ValueKind == JsonValueKind.Object && Num(n, "x") is { } nx)
            {
                double ny = Num(n, "y") ?? 0, nw = Num(n, "w") ?? 0, nh = Num(n, "h") ?? 0;
                double ratio = nx <= 1 && nw <= 1 ? 1 : 100;
                return Scale(new PanelBox(
                    Num(root, "x") ?? nx / ratio * pw, Num(root, "y") ?? ny / ratio * ph,
                    Num(root, "width") ?? nw / ratio * pw, Num(root, "height") ?? nh / ratio * ph, corners, borderless));
            }

            double x = Num(root, "x") ?? 0, y = Num(root, "y") ?? 0;
            double w = Num(root, "width") ?? Num(root, "w") ?? pw * 0.8, h = Num(root, "height") ?? Num(root, "h") ?? ph * 0.35;
            var normalized01 = (root.TryGetProperty("coordinateSpace", out var cs) && cs.ValueKind == JsonValueKind.String
                                && cs.GetString() == "page-normalized")
                               || (x <= 1 && y <= 1 && w <= 1 && h <= 1 && (w > 0 || h > 0));
            return normalized01
                ? Scale(new PanelBox(x * pw, y * ph, w * pw, h * ph, corners, borderless))
                : Scale(new PanelBox(x, y, w, h, corners, borderless));
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    private static double? Num(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}
