using System.Collections.Concurrent;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Domain.Entities.Content;
using SkiaSharp;

namespace PanelForge.Infrastructure.Exports;

/// <summary>
/// Vẽ lớp chữ lên ảnh trang bằng SkiaSharp, cùng quy tắc với FE `roles/letterer/lib/balloonRender.ts` + `textStyle.ts`
/// (sửa một bên thì sửa cả bên kia). Toạ độ element là px trên khổ trang (hoặc % khi width/height ≤ 100, cùng quy tắc FE),
/// được co theo kích thước ảnh trang. Font lấy từ thư mục <c>Exports/Fonts</c> cạnh DLL (vd <c>Mali-Regular.ttf</c>);
/// thiếu file thì dùng font hệ thống cùng tên hoặc font mặc định, nên chữ có thể lệch nhẹ so với trình duyệt.
/// </summary>
public sealed class SkiaPageLetteringCompositor : IPageLetteringCompositor
{
    private const double LetteringPadding = 12; // FE LETTERING_PADDING_PX
    private static readonly string FontDir = Path.Combine(AppContext.BaseDirectory, "Exports", "Fonts");
    private static readonly ConcurrentDictionary<string, SKTypeface> Typefaces = new();

    public ExportPageImage Compose(
        ExportPageImage page, int pageWidthPx, int pageHeightPx, IReadOnlyList<ExportLetteringElement> elements)
    {
        var texts = elements.Where(e => !string.IsNullOrWhiteSpace(e.Text)).OrderBy(e => e.ZIndex).ToList();
        if (texts.Count == 0) return page;

        using var bitmap = SKBitmap.Decode(page.Content)
                           ?? throw new InvalidOperationException($"Ảnh trang {page.PageNumber} hỏng hoặc không đọc được.");
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Scale(bitmap.Width / (float)Math.Max(1, pageWidthPx), bitmap.Height / (float)Math.Max(1, pageHeightPx));
            foreach (var el in texts) DrawElement(canvas, el, pageWidthPx, pageHeightPx);
            canvas.Flush();
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return page with { Content = data.ToArray(), ContentType = "image/png" };
    }

    // ── Giao diện bóng: mặc định theo loại (FE defaultAppearance), ghi đè bằng trường đã lưu trong TextStyle ──

    public sealed record Appearance(string Shape, string FillColor, double FillOpacity, string BorderColor, double BorderWidth);

    public static Appearance ResolveAppearance(string elementType, TextStyle? s)
    {
        var t = elementType.ToUpperInvariant();
        var d = t.Contains("NARRATION") || t.Contains("CAPTION") ? new Appearance("box", "#FFFBEB", 1, "#171717", 2.5)
            : t.Contains("SFX") || t.Contains("SOUND") ? new Appearance("spiky", "#FEF2F2", 1, "#DC2626", 2.5)
            : t.Contains("THOUGHT") ? new Appearance("cloud", "#FFFFFF", 1, "#171717", 2.5)
            : new Appearance("oval", "#FFFFFF", 1, "#171717", 2.5);
        return s is null ? d : new Appearance(
            s.BalloonShape ?? d.Shape, s.FillColor ?? d.FillColor, s.FillOpacity ?? d.FillOpacity,
            s.BorderColor ?? d.BorderColor, s.BorderWidth ?? d.BorderWidth);
    }

    /// <summary>Kiểu chữ hiệu lực: kiểu đã lưu, không có thì preset mặc định theo loại (FE resolveTextStyle).</summary>
    public static TextStyle ResolveTextStyle(string elementType, TextStyle? s) => s ?? elementType switch
    {
        "NARRATION_BOX" => new TextStyle("Be Vietnam Pro", 22, Italic: true, Align: "left", Color: "#171717", LineHeight: 1.3),
        "THOUGHT_BALLOON" => new TextStyle("Patrick Hand", 24, Italic: true, Color: "#171717", LineHeight: 1.3),
        "SOUND_EFFECT" => new TextStyle("Bangers", 56, Bold: true, Color: "#DC2626", StrokeColor: "#FFFFFF", StrokeWidth: 4, LineHeight: 1.0),
        _ => new TextStyle("Mali", 26, Color: "#171717", LineHeight: 1.3)
    };

    public static (double X, double Y, double W, double H) ResolveRect(ExportLetteringElement el, int pageW, int pageH) =>
        el.Width <= 100 || el.Height <= 100
            ? (el.X / 100 * pageW, el.Y / 100 * pageH, el.Width / 100 * pageW, el.Height / 100 * pageH)
            : (el.X, el.Y, el.Width, el.Height);

    private static void DrawElement(SKCanvas canvas, ExportLetteringElement el, int pageW, int pageH)
    {
        var (x, y, w, h) = ResolveRect(el, pageW, pageH);
        var style = ResolveTextStyle(el.ElementType, el.Style);
        var ap = ResolveAppearance(el.ElementType, el.Style);
        double tailX = el.Style?.TailX ?? Math.Round(w / 2), tailY = el.Style?.TailY ?? Math.Round(h + 35);

        canvas.Save();
        canvas.Translate((float)(x + w / 2), (float)(y + h / 2));
        canvas.RotateDegrees((float)el.Rotation);
        canvas.Translate((float)(-w / 2), (float)(-h / 2));

        using (var shape = BalloonPath(ap.Shape, w, h, tailX, tailY))
        {
            if (shape is not null)
            {
                var dots = ap.Shape == "cloud" ? ThoughtTailDots(w, h, tailX, tailY) : [];
                if (ap.FillOpacity > 0)
                {
                    using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = Color(ap.FillColor, ap.FillOpacity) };
                    canvas.DrawPath(shape, fill);
                    foreach (var d in dots) canvas.DrawCircle(d.X, d.Y, d.R, fill);
                }
                if (ap.BorderWidth > 0)
                {
                    using var stroke = new SKPaint
                    {
                        IsAntialias = true, Style = SKPaintStyle.Stroke, Color = Color(ap.BorderColor, 1), StrokeWidth = (float)ap.BorderWidth
                    };
                    canvas.DrawPath(shape, stroke);
                    foreach (var d in dots) canvas.DrawCircle(d.X, d.Y, d.R, stroke);
                }
            }
        }

        using var font = new SKFont(Typeface(style), style.FontSize) { Subpixel = true };
        var layout = Layout(el.Text.Trim(), style, ap.Shape, w, h, s => font.MeasureText(s));
        var align = style.Align switch { "left" => SKTextAlign.Left, "right" => SKTextAlign.Right, _ => SKTextAlign.Center };
        var tx = align switch
        {
            SKTextAlign.Left => layout.Area.X,
            SKTextAlign.Right => layout.Area.X + layout.Area.W,
            _ => layout.Area.X + layout.Area.W / 2
        };
        var metrics = font.Metrics;
        var middleToBaseline = -(metrics.Ascent + metrics.Descent) / 2; // giống textBaseline = 'middle' của canvas
        var top = layout.Area.Y + layout.Area.H / 2 - layout.TextHeight / 2 + layout.LineHeight / 2;

        using var textFill = new SKPaint { IsAntialias = true, Color = Color(style.Color, 1) };
        using var textStroke = style is { StrokeColor: not null, StrokeWidth: > 0 }
            ? new SKPaint
            {
                IsAntialias = true, Style = SKPaintStyle.Stroke, Color = Color(style.StrokeColor, 1),
                StrokeWidth = style.StrokeWidth * 2, StrokeJoin = SKStrokeJoin.Round
            }
            : null;
        for (var i = 0; i < layout.Lines.Count; i++)
        {
            var by = (float)(top + i * layout.LineHeight + middleToBaseline);
            if (textStroke is not null) canvas.DrawText(layout.Lines[i], (float)tx, by, align, font, textStroke);
            canvas.DrawText(layout.Lines[i], (float)tx, by, align, font, textFill);
        }

        canvas.Restore();
    }

    // ── Bố trí chữ (FE textSafeArea / wrapLines / layoutText) ──

    public sealed record TextLayout(
        IReadOnlyList<string> Lines, (double X, double Y, double W, double H) Area, double LineHeight, double TextHeight, bool Overflow);

    private static double Inset(string shape) =>
        shape is "oval" or "cloud" ? Math.Sqrt(0.5) : shape == "spiky" ? 0.82 * Math.Sqrt(0.5) : 1;

    public static (double X, double Y, double W, double H) SafeArea(string shape, double w, double h)
    {
        var k = Inset(shape);
        var pad = k == 1 ? LetteringPadding : 4;
        double aw = Math.Max(1, w * k - pad * 2), ah = Math.Max(1, h * k - pad * 2);
        return ((w - aw) / 2, (h - ah) / 2, aw, ah);
    }

    public static TextLayout Layout(string text, TextStyle style, string shape, double w, double h, Func<string, float> measure)
    {
        var area = SafeArea(shape, w, h);
        var lines = WrapLines(text, area.W, measure);
        var lineHeight = style.FontSize * style.LineHeight;
        var textHeight = lines.Count * lineHeight;
        return new TextLayout(lines, area, lineHeight, textHeight, textHeight > area.H + 1);
    }

    /// <summary>Giữ xuống dòng thủ công, ngắt theo từ, từ quá dài cắt theo ký tự (Unicode scalar, như Array.from).</summary>
    public static List<string> WrapLines(string text, double maxW, Func<string, float> measure)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Length > 0 ? $"{line} {word}" : word;
                if (measure(candidate) <= maxW) { line = candidate; continue; }
                if (line.Length > 0) lines.Add(line);
                line = "";
                if (measure(word) <= maxW) { line = word; continue; }
                foreach (var rune in word.EnumerateRunes())
                {
                    var ch = rune.ToString();
                    if (line.Length > 0 && measure(line + ch) > maxW) { lines.Add(line); line = ch; }
                    else line += ch;
                }
            }
            lines.Add(line);
        }
        return lines;
    }

    // ── Hình bóng (FE createSpeechBalloonPath / createThoughtBalloonPath / createShoutBalloonPath) ──

    public static SKPath? BalloonPath(string shape, double w, double h, double tailX, double tailY)
    {
        double cx = w / 2, cy = h / 2, rx = w / 2, ry = h / 2;
        switch (shape)
        {
            case "oval":
            {
                var p = new SKPath();
                double dx = tailX - cx, dy = tailY - cy;
                if (Math.Sqrt(dx * dx + dy * dy) <= Math.Min(rx, ry) * 0.95)
                {
                    p.AddOval(SKRect.Create(0, 0, (float)w, (float)h));
                    return p;
                }
                var angle = Math.Atan2(dy, dx);
                var baseAngle = Math.Min(0.35, Math.Max(0.14, 20 / Math.Max(rx, ry)));
                double a1 = angle + baseAngle, a2 = angle - baseAngle;
                p.MoveTo((float)(cx + rx * Math.Cos(a1)), (float)(cy + ry * Math.Sin(a1)));
                p.ArcTo((float)rx, (float)ry, 0, SKPathArcSize.Large, SKPathDirection.Clockwise,
                    (float)(cx + rx * Math.Cos(a2)), (float)(cy + ry * Math.Sin(a2)));
                p.LineTo((float)tailX, (float)tailY);
                p.Close();
                return p;
            }
            case "cloud":
            {
                const int lobes = 12;
                var pts = Enumerable.Range(0, lobes).Select(i =>
                {
                    var th = i * 2 * Math.PI / lobes;
                    return new SKPoint(
                        (float)(cx + rx * (1 + 0.05 * Math.Sin(th * lobes)) * Math.Cos(th)),
                        (float)(cy + ry * (1 + 0.05 * Math.Cos(th * lobes)) * Math.Sin(th)));
                }).ToList();
                var p = new SKPath();
                p.MoveTo(pts[0]);
                for (var i = 0; i < lobes; i++)
                {
                    var mid = (i + 0.5) * 2 * Math.PI / lobes;
                    p.QuadTo(new SKPoint((float)(cx + rx * 1.12 * Math.Cos(mid)), (float)(cy + ry * 1.12 * Math.Sin(mid))), pts[(i + 1) % lobes]);
                }
                p.Close();
                return p;
            }
            case "spiky":
            {
                const int spikes = 18;
                var p = new SKPath();
                for (var i = 0; i < spikes * 2; i++)
                {
                    var a = i * Math.PI / spikes;
                    var f = i % 2 == 0 ? 1.05 : 0.82;
                    var pt = new SKPoint((float)(cx + rx * f * Math.Cos(a)), (float)(cy + ry * f * Math.Sin(a)));
                    if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
                }
                p.Close();
                return p;
            }
            case "box":
            {
                var p = new SKPath();
                p.AddRect(SKRect.Create(0, 0, (float)w, (float)h));
                return p;
            }
            default:
                return null;
        }
    }

    private static List<(float X, float Y, float R)> ThoughtTailDots(double w, double h, double tailX, double tailY)
    {
        double cx = w / 2, cy = h / 2;
        var angle = Math.Atan2(tailY - cy, tailX - cx);
        double sx = cx + w / 2 * 0.95 * Math.Cos(angle), sy = cy + h / 2 * 0.95 * Math.Sin(angle);
        return new[] { (T: 0.3, R: 8f), (T: 0.65, R: 5f), (T: 0.95, R: 3.5f) }
            .Select(d => ((float)(sx + (tailX - sx) * d.T), (float)(sy + (tailY - sy) * d.T), d.R)).ToList();
    }

    // ── Màu, font ──

    private static SKColor Color(string hex, double opacity)
    {
        var c = SKColor.TryParse(hex, out var parsed) ? parsed : SKColors.Black;
        return c.WithAlpha((byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255));
    }

    private static SKTypeface Typeface(TextStyle s)
    {
        var variant = (s.Bold, s.Italic) switch
        {
            (true, true) => "BoldItalic", (true, false) => "Bold", (false, true) => "Italic", _ => "Regular"
        };
        return Typefaces.GetOrAdd($"{s.FontFamily}|{variant}", _ =>
        {
            var name = s.FontFamily.Replace(" ", "");
            var file = Path.Combine(FontDir, $"{name}-{variant}.ttf");
            var regular = Path.Combine(FontDir, $"{name}-Regular.ttf");
            var style = new SKFontStyle(
                s.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal,
                s.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
            return (File.Exists(file) ? SKTypeface.FromFile(file) : null)
                   ?? (File.Exists(regular) ? SKTypeface.FromFile(regular) : null)
                   ?? SKFontManager.Default.MatchFamily(s.FontFamily, style)
                   ?? SKTypeface.Default;
        });
    }
}
