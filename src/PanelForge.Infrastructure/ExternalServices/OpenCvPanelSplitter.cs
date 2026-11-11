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
            panels.Select((p, i) => new SplitPanelDto(i + 1, [p.Box.X, p.Box.Y, p.Box.Width, p.Box.Height],
                p.Polygon.Select(pt => new[] { pt.X, pt.Y }).ToArray())).ToList(),
            bubbles);
    }

    /// <summary>Khung tìm được: hộp bao (để sắp thứ tự đọc, giữ parity với app.py) + đa giác thật (khung cắt chéo).</summary>
    private sealed record PanelShape(Rect Box, Point[] Polygon);

    // Panels: viền tối → contour ngoài cùng → bbox đủ lớn; thứ tự đọc theo hàng (y), trong hàng theo x (đảo nếu RTL)
    private static List<PanelShape> FindPanels(Mat gray, int W, int H, bool rightToLeft)
    {
        // Otsu thay ngưỡng cứng 100: viền xám / scan nhạt vẫn tách được; kẹp [100, 200] để trang gần trắng tinh không bị nhiễu
        using var dark = new Mat();
        var otsu = Cv2.Threshold(gray, dark, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
        Cv2.Threshold(gray, dark, Math.Clamp(otsu, 100, 200), 255, ThresholdTypes.BinaryInv);
        // Đóng nét đứt 1-2px trên viền khung (scan / nén JPEG) để contour không hở
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
        Cv2.MorphologyEx(dark, dark, MorphTypes.Close, kernel);
        // Tree: contour gốc (Parent = -1) đúng bằng contour của External, nhưng đọc thêm được các "lỗ" bên trong khung
        Cv2.FindContours(dark, out var contours, out var hierarchy, RetrievalModes.Tree, ContourApproximationModes.ApproxSimple);

        // ponytail: ngưỡng diện tích 4% cố định; panel rất nhỏ / không viền cần model (Magi)
        var minArea = 0.04 * W * H;
        var found = new List<PanelShape>();
        for (var i = 0; i < contours.Length; i++)
        {
            if (hierarchy[i].Parent != -1) continue;
            var box = Cv2.BoundingRect(contours[i]);
            if ((long)box.Width * box.Height < minArea) continue;

            var parts = SharedBorderParts(contours, hierarchy, i, minArea);
            if (parts.Count >= 2)
                found.AddRange(parts.Select(c => new PanelShape(Cv2.BoundingRect(c), Simplify(c))));
            else
                found.Add(new PanelShape(box, Simplify(contours[i])));
        }

        var panels = found.OrderBy(p => p.Box.Y).ToList(); // OrderBy ổn định như list.sort của Python
        var rows = new List<List<PanelShape>>();
        var row = new List<PanelShape>();
        foreach (var p in panels)
        {
            if (row.Count > 0 && p.Box.Y > row[0].Box.Y + row[0].Box.Height * 0.5)
            {
                rows.Add(row);
                row = [];
            }
            row.Add(p);
        }
        if (row.Count > 0) rows.Add(row);

        return rows.SelectMany(r => rightToLeft ? r.OrderBy(p => -p.Box.X) : r.OrderBy(p => p.Box.X)).ToList();
    }

    /// <summary>
    /// Nhiều khung dùng chung một đường viền (vd khung chữ nhật bị một nét chéo chia đôi) tạo thành MỘT contour ngoài.
    /// Khi đó ruột các khung là các "lỗ" của contour: nhận là khung riêng nếu có ≥ 2 lỗ lớn, mỗi lỗ là hình lồi sạch
    /// và cộng lại phủ phần lớn khối. Lỗ bị nét vẽ chia vụn (khung có tranh) thì không đạt → giữ nguyên một khung.
    /// </summary>
    // ponytail: heuristic hình học; một nét vẽ thẳng chạy suốt khung (đường chân trời) có thể bị hiểu là rãnh chia khung —
    // Artist xoá/gộp ở bước rà soát; trường hợp khó cần model (Magi)
    private static List<Point[]> SharedBorderParts(Point[][] contours, HierarchyIndex[] hierarchy, int outer, double minArea)
    {
        var holes = new List<Point[]>();
        for (var h = hierarchy[outer].Child; h != -1; h = hierarchy[h].Next)
            if (Cv2.ContourArea(contours[h]) >= minArea) holes.Add(contours[h]);

        if (holes.Count < 2) return [];
        var clean = holes.All(c => Cv2.ContourArea(c) / Cv2.ContourArea(Cv2.ConvexHull(c)) >= 0.9);
        var coverage = holes.Sum(c => Cv2.ContourArea(c)) / Cv2.ContourArea(contours[outer]);
        return clean && coverage >= 0.6 ? holes : [];
    }

    // Đa giác ít đỉnh của khung: khung chữ nhật → 4 đỉnh, khung cắt chéo → hình thang / tam giác...
    private static Point[] Simplify(Point[] contour) =>
        Cv2.ApproxPolyDP(contour, 0.01 * Cv2.ArcLength(contour, true), true);

    // Bong bóng: vùng sáng kín, gần elip, nằm trong một panel
    private static List<SplitBubbleDto> FindBubbles(Mat gray, int W, int H, List<PanelShape> panels)
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
            // Bong bóng có đuôi (cắt đuôi rồi xét elip) / caption chữ nhật (khối đặc) → nhận nếu có chữ bên trong
            // ponytail: heuristic mật độ điểm tối; bong bóng dạng mây/răng cưa hay chữ viết tay rời rạc cần model (Magi)
            var ellipseLike = ratio > 0.85 && ratio < 1.15;
            if (!ellipseLike
                && !((area / Cv2.ContourArea(Cv2.ConvexHull(c)) >= 0.85 || BodyIsEllipse(c, r)) && HasText(gray, c, r)))
                continue;

            double cx = r.X + r.Width / 2.0, cy = r.Y + r.Height / 2.0;
            // Hộp bao của 2 khung cắt chéo chồng lên nhau → xét tâm bong bóng theo đa giác thật trước
            var idx = panels.FindIndex(p => Cv2.PointPolygonTest(p.Polygon, new Point2f((float)cx, (float)cy), false) >= 0);
            if (idx < 0)
                idx = panels.FindIndex(p => p.Box.X <= cx && cx <= p.Box.X + p.Box.Width && p.Box.Y <= cy && cy <= p.Box.Y + p.Box.Height);
            if (idx < 0 || (long)r.Width * r.Height > 0.5 * panels[idx].Box.Width * panels[idx].Box.Height) continue;

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

    // Mở hình thái với kernel ~1/4 cạnh ngắn: đuôi mảnh bị cắt, thân bong bóng giữ lại → xét elip trên khối lớn nhất
    private static bool BodyIsEllipse(Point[] contour, Rect r)
    {
        using var mask = new Mat(r.Height, r.Width, MatType.CV_8UC1, Scalar.Black);
        Cv2.DrawContours(mask, [contour.Select(p => new Point(p.X - r.X, p.Y - r.Y)).ToArray()], 0, Scalar.White, -1);
        var k = Math.Max(3, Math.Min(r.Width, r.Height) / 4) | 1;
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(k, k));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Open, kernel);
        Cv2.FindContours(mask, out var parts, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        var body = parts.Where(p => p.Length >= 5).MaxBy(p => Cv2.ContourArea(p));
        if (body is null) return false;
        var e = Cv2.FitEllipse(body);
        var ratio = Cv2.ContourArea(body) / (Math.PI * e.Size.Width * e.Size.Height / 4);
        return ratio is > 0.85 and < 1.15;
    }

    // Tỉ lệ điểm tối bên trong contour (lỗ chữ tính vào vì vẽ mask đặc): chữ ≈ 2–40%; trống trơn hay hình vẽ dày thì không
    private static bool HasText(Mat gray, Point[] contour, Rect r)
    {
        using var mask = new Mat(r.Height, r.Width, MatType.CV_8UC1, Scalar.Black);
        Cv2.DrawContours(mask, [contour.Select(p => new Point(p.X - r.X, p.Y - r.Y)).ToArray()], 0, Scalar.White, -1);
        using var roi = new Mat(gray, r);
        using var ink = new Mat();
        Cv2.Threshold(roi, ink, 128, 255, ThresholdTypes.BinaryInv);
        Cv2.Erode(mask, mask, null, iterations: 3); // bỏ viền bong bóng khỏi phép đếm
        Cv2.BitwiseAnd(ink, mask, ink);
        double inside = Cv2.CountNonZero(mask);
        if (inside == 0) return false;
        var density = Cv2.CountNonZero(ink) / inside;
        return density is > 0.02 and < 0.4;
    }
}
