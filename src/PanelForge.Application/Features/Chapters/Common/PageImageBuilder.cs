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
        Guid pageId, IReadOnlyCollection<Guid> assetReferenceIds, CancellationToken ct, string? languageCode = null,
        long? canvasStreamVersion = null)
    {
        // Snapshot có thể trỏ tới trang/Panel đã xoá sau khi duyệt: bỏ bộ lọc xoá mềm để dựng lại đúng bản đã duyệt
        var page = await db.Pages.IgnoreQueryFilters().AsNoTracking().Where(p => p.Id == pageId)
            .Select(p => new { p.ChapterId, p.PageNumber, p.WidthPx, p.HeightPx }).FirstOrDefaultAsync(ct);
        if (page is null) return null;

        var refs = await (
            from r in db.AssetReferences.IgnoreQueryFilters().AsNoTracking()
            where assetReferenceIds.Contains(r.Id) && r.PageId == pageId
            join a in db.Assets.AsNoTracking() on r.AssetId equals a.Id
            select new { r.Id, r.PanelId, a.StorageKey, a.ContentType }).ToListAsync(ct);
        if (refs.Count == 0) return null;

        // BR-01/BR-12/BR-17: dựng đúng bản đã duyệt. ChapterSnapshot nào chứa đúng bộ artwork này của trang thì lấy khổ trang,
        // toạ độ khung và version canvas đã ghim trong đó — mở lại chương rồi sửa bố cục / lớp chữ không lọt vào link xem
        // thử, job xuất hay Reader của bản cũ. Không có snapshot (dữ liệu cũ / chương chưa duyệt) thì dùng dữ liệu sống.
        var pin = await FindPinAsync(db, page.ChapterId, pageId, refs.Select(r => r.Id).ToHashSet(), ct);
        int width = pin?.WidthPx ?? (page.WidthPx > 0 ? page.WidthPx : 1200);
        int height = pin?.HeightPx ?? (page.HeightPx > 0 ? page.HeightPx : 1800);

        ExportPageImage image;
        var whole = pin is not null
            ? refs.FirstOrDefault(r => r.Id == pin.ArtworkReferenceId)
            : refs.FirstOrDefault(r => r.PanelId == null);
        if (whole is not null)
        {
            var bytes = await ReadAsync(storage, whole.StorageKey, ct);
            if (bytes is null) return null;
            image = new ExportPageImage(page.PageNumber, bytes, whole.ContentType);
        }
        else
        {
            // (artwork, BoundingBox) theo thứ tự khung: bản ghim lấy hộp đã chụp, không đọc Panel đang sống
            List<(string StorageKey, string? Box)> parts;
            if (pin is not null)
            {
                parts = pin.PanelArtworks!
                    .Select(pa => (Ref: refs.FirstOrDefault(r => r.Id == pa.ArtworkReferenceId), pa.BoundingBox))
                    .Where(x => x.Ref is not null).Select(x => (x.Ref!.StorageKey, x.BoundingBox)).ToList();
            }
            else
            {
                var panelIds = refs.Where(r => r.PanelId != null).Select(r => r.PanelId!.Value).ToList();
                var panels = await db.Panels.IgnoreQueryFilters().AsNoTracking().Where(p => panelIds.Contains(p.Id))
                    .OrderBy(p => p.PanelNumber).Select(p => new { p.Id, p.BoundingBox }).ToListAsync(ct);
                parts = panels.Select(p => (refs.First(r => r.PanelId == p.Id).StorageKey, (string?)p.BoundingBox)).ToList();
            }

            var layers = new List<PagePanelLayer>();
            foreach (var (key, boxJson) in parts)
            {
                var box = PanelBox.Parse(boxJson, width, height);
                layers.Add(new PagePanelLayer(await ReadAsync(storage, key, ct),
                    box.X, box.Y, box.Width, box.Height, box.Corners, box.Borderless));
            }
            image = compositor.ComposePanels(page.PageNumber, width, height, layers);
        }

        return await WithLetteringAsync(pageRepository, compositor, image, pageId, ct, languageCode,
            canvasStreamVersion, pin?.CanvasStreamVersion);
    }

    /// <summary>
    /// Trang trong ChapterSnapshot mới nhất có đúng bộ artwork <paramref name="pageRefIds"/> và đủ dữ liệu ghim (khổ trang,
    /// toạ độ khung). Null nếu không có — snapshot cũ chưa chụp toạ độ cũng trả null để dùng dữ liệu sống.
    /// </summary>
    // ponytail: đọc và giải mã manifest của mọi snapshot chương cho mỗi trang; chương có nhiều lần duyệt lại thì cache theo chapterId.
    internal static async Task<SnapshotPage?> FindPinAsync(
        IPanelForgeDbContext db, Guid chapterId, Guid pageId, IReadOnlySet<Guid> pageRefIds, CancellationToken ct)
    {
        var manifests = await db.ChapterSnapshots.AsNoTracking().Where(s => s.ChapterId == chapterId)
            .OrderByDescending(s => s.SnapshotNumber).Select(s => s.ManifestJson).ToListAsync(ct);
        foreach (var json in manifests)
        {
            SnapshotManifest? manifest;
            try { manifest = PanelForge.AI.Contracts.AiJson.Deserialize<SnapshotManifest>(json); }
            catch (JsonException) { continue; }
            var sp = manifest?.Pages.FirstOrDefault(p => p.PageId == pageId);
            if (sp?.WidthPx is null || sp.HeightPx is null) continue;
            var ids = sp.ArtworkReferenceId is { } w
                ? new HashSet<Guid> { w }
                : (sp.PanelArtworks ?? []).Select(a => a.ArtworkReferenceId).ToHashSet();
            if (ids.Count > 0 && ids.SetEquals(pageRefIds)) return sp;
        }
        return null;
    }

    /// <summary>
    /// Vẽ lớp chữ của Page lên ảnh trang (bóng thoại, dẫn truyện, SFX), theo ngôn ngữ phát hành nếu có (BR-34).
    /// Version canvas: ưu tiên version người gọi truyền (snapshot ấn bản ngôn ngữ; âm = dùng bản hiện tại), không có thì
    /// version ghim trong ChapterSnapshot (-1 = trang chưa có event nào lúc duyệt → không có chữ), không ghim thì bản hiện tại
    /// (BR-01/BR-12/BR-17: mở lại chương rồi sửa chữ không lọt vào bản đã duyệt).
    /// </summary>
    private static async Task<ExportPageImage> WithLetteringAsync(
        IPageRepository pageRepository, IPageLetteringCompositor compositor, ExportPageImage image, Guid pageId, CancellationToken ct,
        string? languageCode = null, long? canvasStreamVersion = null, long? pinnedVersion = null)
    {
        long? version = canvasStreamVersion;
        if (version is null)
        {
            if (pinnedVersion is < 0) return image;
            version = pinnedVersion;
        }
        var page = version is >= 0
            ? await pageRepository.GetAtVersionAsync(pageId, version.Value, ct)
            : await pageRepository.GetAsync(pageId, ct);
        if (page is null) return image;
        var texts = page.Elements.Values
            .Where(e => !e.IsRemoved && Page.TextElementTypes.Contains(e.ElementType))
            .Select(e =>
            {
                string text = "";
                if (!string.IsNullOrEmpty(languageCode) && e.Texts != null && e.Texts.TryGetValue(languageCode, out var trans) && !string.IsNullOrWhiteSpace(trans))
                {
                    text = trans;
                }
                else
                {
                    text = e.RenderedText ?? e.Content ?? "";
                }
                return new ExportLetteringElement(e.ElementType, e.X, e.Y, e.Width, e.Height, e.Rotation, e.ZIndex, text, e.Style);
            })
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
