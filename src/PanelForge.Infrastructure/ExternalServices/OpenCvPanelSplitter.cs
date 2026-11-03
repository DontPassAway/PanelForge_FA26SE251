using OpenCvSharp;
using PanelForge.Application.Common.Interfaces.Services;

namespace PanelForge.Infrastructure.ExternalServices;

/// <summary>
/// Tách panel + bong bóng thoại từ ảnh một trang manga bằng OpenCV, chạy trong tiến trình .NET.
/// Port 1:1 từ <c>src/PanelForge.PanelSplitter/app.py</c> (giữ file đó làm nguồn đối chiếu parity): cùng ngưỡng, cùng thứ tự.
/// Stateless, an toàn đa luồng (mỗi lần gọi dùng Mat riêng) → đăng ký Singleton.
/// </summary>
public sealed class OpenCvPanelSplitter : IPanelSplitter
{
    public async Task<PanelSplitResult> SplitAsync(
        Stream image, string fileName, bool rightToLeft, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await image.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        // CPU nặng (ảnh tới 20MB): không chạy trên thread của request
        return await Task.Run(() => Split(bytes, rightToLeft, cancellationToken), cancellationToken);
    }

    internal static PanelSplitResult Split(byte[] bytes, bool rightToLeft, CancellationToken ct = default)
    {
        if (bytes.Length == 0) throw new InvalidImageException("File không phải ảnh hợp lệ.");

        using var img = Cv2.ImDecode(bytes, ImreadModes.Color);
        if (img.Empty()) throw new InvalidImageException("File không phải ảnh hợp lệ.");

        int W = img.Width, H = img.Height;
        using var gray = new Mat();
        Cv2.CvtColor(img, gray, ColorConversionCodes.BGR2GRAY);

        var panels = FindPanels(gray, W, H, rightToLeft);
        ct.ThrowIfCancellationRequested();
        var bubbles = FindBubbles(gray, W, H, panels);

        return new PanelSplitResult(W, H,
            panels.Select((p, i) => new SplitPanelDto(i + 1, [p.X, p.Y, p.Width, p.Height])).ToList(),
            bubbles);
    }

    // Panels: viền tối → contour ngoài cùng → bbox đủ lớn; thứ tự đọc theo hàng (y), trong hàng theo x (đảo nếu RTL)
    private static List<Rect> FindPanels(Mat gray, int W, int H, bool rightToLeft)
    {
        using var dark = new Mat();
        Cv2.Threshold(gray, dark, 100, 255, ThresholdTypes.BinaryInv);
        Cv2.FindContours(dark, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        // ponytail: ngưỡng diện tích 4% cố định; panel rất nhỏ / không viền cần model (Magi)
        var panels = contours.Select(Cv2.BoundingRect)
            .Where(r => (long)r.Width * r.Height >= 0.04 * W * H)
            .OrderBy(r => r.Y) // OrderBy ổn định như list.sort của Python
            .ToList();

        var rows = new List<List<Rect>>();
        var row = new List<Rect>();
        foreach (var p in panels)
        {
            if (row.Count > 0 && p.Y > row[0].Y + row[0].Height * 0.5)
            {
                rows.Add(row);
                row = [];
            }
            row.Add(p);
        }
        if (row.Count > 0) rows.Add(row);

        return rows.SelectMany(r => rightToLeft ? r.OrderBy(p => -p.X) : r.OrderBy(p => p.X)).ToList();
    }

    // Bong bóng: vùng sáng kín, gần elip, nằm trong một panel
    private static List<SplitBubbleDto> FindBubbles(Mat gray, int W, int H, List<Rect> panels)
    {
        using var light = new Mat();
        Cv2.Threshold(gray, light, 200, 255, ThresholdTypes.Binary);
        Cv2.FindContours(light, out var contours, out _, RetrievalModes.CComp, ContourApproximationModes.ApproxSimple);

        var bubbles = new List<SplitBubbleDto>();
        foreach (var c in contours)
        {
            var area = Cv2.ContourArea(c);
            if (area < 0.003 * W * H || c.Length < 5) continue;

            var r = Cv2.BoundingRect(c);
            var ellipse = Cv2.FitEllipse(c);
            var ratio = area / (Math.PI * ellipse.Size.Width * ellipse.Size.Height / 4);
            if (!(ratio > 0.85 && ratio < 1.15)) continue;

            double cx = r.X + r.Width / 2.0, cy = r.Y + r.Height / 2.0;
            var idx = panels.FindIndex(p => p.X <= cx && cx <= p.X + p.Width && p.Y <= cy && cy <= p.Y + p.Height);
            if (idx < 0 || (long)r.Width * r.Height > 0.5 * panels[idx].Width * panels[idx].Height) continue;

            var poly = Cv2.ApproxPolyDP(c, 2, true).Select(pt => new[] { pt.X, pt.Y }).ToArray();
            bubbles.Add(new SplitBubbleDto(idx + 1, [r.X, r.Y, r.Width, r.Height], poly));
        }

        // Viền bong bóng cho 2 contour (trong/ngoài) → giữ cái nằm trong
        static bool Inside(int[] a, int[] b) =>
            a[0] >= b[0] && a[1] >= b[1] && a[0] + a[2] <= b[0] + b[2] && a[1] + a[3] <= b[1] + b[3];

        return bubbles
            .Where(b => !bubbles.Any(o => !ReferenceEquals(o, b) && Inside(o.Bbox, b.Bbox)))
            .OrderBy(b => b.PanelOrder).ThenBy(b => b.Bbox[1])
            .ToList();
    }
}
