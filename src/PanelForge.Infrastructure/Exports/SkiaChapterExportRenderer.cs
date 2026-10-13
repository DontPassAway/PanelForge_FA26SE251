using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Domain.Enums;
using SkiaSharp;

namespace PanelForge.Infrastructure.Exports;

/// <summary>
/// Dựng file xuất chương bằng SkiaSharp (MIT): CBZ (ZIP ảnh + ComicInfo.xml), ảnh trang (ZIP), PDF mỗi trang một ảnh, và
/// webtoon cuộn dọc (một ảnh JPEG). Trang PNG/JPEG giữ nguyên bytes; WebP đổi sang PNG để trình đọc cũ mở được.
/// Từ preset chỉ dùng: PDF "resolution" (dpi, mặc định 300); WEBTOON "stripWidth" (800), "quality" (85).
/// shortcut: không làm CMYK/bleed (in ấn chuyên nghiệp là mở rộng); làm khi có yêu cầu in.
/// </summary>
public sealed class SkiaChapterExportRenderer : IChapterExportRenderer
{
    /// <summary>Giới hạn chiều cao ảnh JPEG.</summary>
    public const int MaxWebtoonHeight = 65_000;

    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    public bool Supports(string format) => format.ToUpperInvariant() is "CBZ" or "PAGE_IMAGES" or "PDF" or "WEBTOON";

    public ExportFile Render(string format, ExportManifest manifest, IReadOnlyList<ExportPageImage> pages)
    {
        if (pages.Count == 0) throw new InvalidOperationException("Bản xuất phải có ít nhất một trang.");
        var config = ReadConfig(manifest.ConfigOptionsJson);

        return format.ToUpperInvariant() switch
        {
            "CBZ" => new ExportFile(Zip(pages, ComicInfo(manifest, pages.Count)), "application/vnd.comicbook+zip", "cbz"),
            "PAGE_IMAGES" => new ExportFile(Zip(pages, null), "application/zip", "zip"),
            "PDF" => new ExportFile(Pdf(manifest, pages, Dpi(config)), "application/pdf", "pdf"),
            "WEBTOON" => new ExportFile(
                Webtoon(pages, Int(config, "stripWidth", 800, 100, 4000), Int(config, "quality", 85, 1, 100)), "image/jpeg", "jpg"),
            _ => throw new InvalidOperationException($"Định dạng {format} chưa được hỗ trợ xuất.")
        };
    }

    private static byte[] Zip(IReadOnlyList<ExportPageImage> pages, XDocument? comicInfo)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Đánh số theo thứ tự trong bản xuất (không theo số trang) để trình đọc sắp đúng
            for (var i = 0; i < pages.Count; i++)
            {
                var (bytes, ext) = Portable(pages[i]);
                using var entry = zip.CreateEntry($"{i + 1:D3}.{ext}", CompressionLevel.NoCompression).Open();
                entry.Write(bytes);
            }

            if (comicInfo is not null)
            {
                using var entry = zip.CreateEntry("ComicInfo.xml").Open();
                comicInfo.Save(entry);
            }
        }

        return buffer.ToArray();
    }

    /// <summary>ComicInfo 2.0: Manga = YesAndRightToLeft để trình đọc lật trang phải sang trái theo hướng đọc Series.</summary>
    private static XDocument ComicInfo(ExportManifest m, int pageCount) => new(
        new XDeclaration("1.0", "utf-8", null),
        new XElement("ComicInfo",
            m.ChapterTitle is { Length: > 0 } title ? new XElement("Title", title) : null,
            new XElement("Series", m.SeriesTitle),
            new XElement("Number", m.ChapterNumber.ToString(CultureInfo.InvariantCulture)),
            new XElement("PageCount", pageCount),
            new XElement("Manga", m.ReadingDirection == ReadingDirection.RightToLeft ? "YesAndRightToLeft" : "No")));

    private static byte[] Pdf(ExportManifest m, IReadOnlyList<ExportPageImage> pages, int dpi)
    {
        using var buffer = new MemoryStream();
        var metadata = new SKDocumentPdfMetadata
        {
            Title = m.ChapterTitle is { Length: > 0 } t ? $"{m.SeriesTitle} - {m.ChapterNumber}: {t}" : $"{m.SeriesTitle} - {m.ChapterNumber}",
            Creator = "PanelForge",
            RasterDpi = dpi
        };
        using (var document = SKDocument.CreatePdf(buffer, metadata))
        {
            foreach (var page in pages)
            {
                using var image = Decode(page);
                float width = image.Width * 72f / dpi, height = image.Height * 72f / dpi;
                var canvas = document.BeginPage(width, height);
                canvas.DrawImage(image, SKRect.Create(width, height), Sampling);
                document.EndPage();
            }

            document.Close();
        }

        return buffer.ToArray();
    }

    private static byte[] Webtoon(IReadOnlyList<ExportPageImage> pages, int width, int quality)
    {
        var images = pages.Select(Decode).ToList();
        try
        {
            var heights = images.Select(i => (int)Math.Round(i.Height * (double)width / i.Width)).ToList();
            var total = heights.Sum();
            if (total > MaxWebtoonHeight)
                throw new InvalidOperationException(
                    $"Chương quá dài cho một ảnh webtoon ({total}px > {MaxWebtoonHeight}px ở chiều rộng {width}px).");

            using var surface = SKSurface.Create(new SKImageInfo(width, total, SKColorType.Rgba8888, SKAlphaType.Premul))
                                ?? throw new InvalidOperationException("Không đủ bộ nhớ để ghép ảnh webtoon.");
            surface.Canvas.Clear(SKColors.White);
            var y = 0;
            for (var i = 0; i < images.Count; i++)
            {
                surface.Canvas.DrawImage(images[i], SKRect.Create(0, y, width, heights[i]), Sampling);
                y += heights[i];
            }

            using var snapshot = surface.Snapshot();
            using var data = snapshot.Encode(SKEncodedImageFormat.Jpeg, quality);
            return data.ToArray();
        }
        finally
        {
            images.ForEach(i => i.Dispose());
        }
    }

    private static (byte[] Bytes, string Ext) Portable(ExportPageImage page) => page.ContentType switch
    {
        "image/png" => (page.Content, "png"),
        "image/jpeg" => (page.Content, "jpg"),
        _ => (Encode(page, SKEncodedImageFormat.Png), "png")
    };

    private static byte[] Encode(ExportPageImage page, SKEncodedImageFormat format)
    {
        using var image = Decode(page);
        using var data = image.Encode(format, 100);
        return data.ToArray();
    }

    private static SKImage Decode(ExportPageImage page) =>
        SKImage.FromEncodedData(page.Content)
        ?? throw new InvalidOperationException($"Ảnh trang {page.PageNumber} hỏng hoặc không đọc được.");

    private static JsonElement? ReadConfig(string json)
    {
        try { return JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json).RootElement.Clone(); }
        catch (JsonException) { return null; }
    }

    private static int Int(JsonElement? config, string name, int fallback, int min, int max) =>
        config is { ValueKind: JsonValueKind.Object } c && c.TryGetProperty(name, out var v) && v.TryGetInt32(out var n)
            ? Math.Clamp(n, min, max)
            : fallback;

    /// <summary>"resolution": "300dpi" → 300.</summary>
    private static int Dpi(JsonElement? config) =>
        config is { ValueKind: JsonValueKind.Object } c && c.TryGetProperty("resolution", out var v) && v.ValueKind == JsonValueKind.String
        && int.TryParse(new string(v.GetString()!.TakeWhile(char.IsDigit).ToArray()), out var dpi)
            ? Math.Clamp(dpi, 72, 1200)
            : 300;
}
