using FluentAssertions;
using OpenCvSharp;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Infrastructure.ExternalServices;
using Xunit;

namespace PanelForge.UnitTests.Infrastructure;

public class OpenCvPanelSplitterTests
{
    // Trang 900x600: hàng trên 2 khung, hàng dưới 1 khung rộng; một bong bóng elip trong khung bên phải hàng trên
    private static byte[] SyntheticPage()
    {
        using var img = new Mat(600, 900, MatType.CV_8UC3, Scalar.White);
        Cv2.Rectangle(img, new Rect(20, 20, 420, 260), Scalar.Black, 4);
        Cv2.Rectangle(img, new Rect(460, 20, 420, 260), Scalar.Black, 4);
        Cv2.Rectangle(img, new Rect(20, 320, 860, 260), Scalar.Black, 4);
        Cv2.Ellipse(img, new Point(670, 150), new Size(80, 50), 0, 0, 360, Scalar.Black, 3);
        Cv2.ImEncode(".png", img, out var png);
        return png;
    }

    private static Task<PanelSplitResult> Split(byte[] bytes, bool rtl) =>
        new OpenCvPanelSplitter().SplitAsync(new MemoryStream(bytes), "page.png", rtl);

    [Fact]
    public async Task LeftToRight_FindsPanelsInReadingOrder_AndBubbleInRightPanel()
    {
        var result = await Split(SyntheticPage(), rtl: false);

        result.Width.Should().Be(900);
        result.Height.Should().Be(600);
        result.Panels.Select(p => p.Order).Should().Equal(1, 2, 3);
        result.Panels.Select(p => p.Bbox[0]).Should().Equal(18, 458, 18); // viền dày 4 → bbox lệch 2px ra ngoài
        result.Panels[2].Bbox[1].Should().BeGreaterThan(300);

        var bubble = result.Bubbles.Should().ContainSingle().Subject;
        bubble.PanelOrder.Should().Be(2);
        var cx = bubble.Bbox[0] + bubble.Bbox[2] / 2;
        cx.Should().BeInRange(660, 680);
        bubble.Polygon.Length.Should().BeGreaterThan(4);
    }

    [Fact]
    public async Task RightToLeft_ReversesOrderWithinRow()
    {
        var result = await Split(SyntheticPage(), rtl: true);

        result.Panels.Select(p => p.Bbox[0]).Should().Equal(458, 18, 18);
        result.Bubbles.Should().ContainSingle().Which.PanelOrder.Should().Be(1); // khung phải đọc trước
    }

    // Parity với app.py: expected.json do split_page (Python, OpenCV) sinh ra trên đúng các ảnh này
    // (script sinh: xem TestData/panel-splitter/README.md). Bbox bong bóng cho phép lệch ±1px do khác bản OpenCV.
    [Theory]
    [InlineData("grid", false)]
    [InlineData("grid", true)]
    [InlineData("noisy", false)]
    [InlineData("noisy", true)]
    [InlineData("large", false)]
    [InlineData("large", true)]
    public async Task MatchesPythonReferenceOutput(string page, bool rtl)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "TestData", "panel-splitter");
        using var doc = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(dir, "expected.json")));
        var exp = doc.RootElement.GetProperty(page).GetProperty(rtl ? "rtl" : "ltr");

        var result = await Split(await File.ReadAllBytesAsync(Path.Combine(dir, page + ".png")), rtl);

        result.Width.Should().Be(exp.GetProperty("width").GetInt32());
        result.Height.Should().Be(exp.GetProperty("height").GetInt32());
        var expPanels = exp.GetProperty("panels").EnumerateArray()
            .Select(p => p.GetProperty("bbox").EnumerateArray().Select(v => v.GetInt32()).ToArray()).ToList();
        result.Panels.Select(p => p.Bbox).Should().BeEquivalentTo(expPanels, o => o.WithStrictOrdering());

        var expBubbles = exp.GetProperty("bubbles").EnumerateArray().ToList();
        result.Bubbles.Should().HaveCount(expBubbles.Count);
        for (var i = 0; i < expBubbles.Count; i++)
        {
            result.Bubbles[i].PanelOrder.Should().Be(expBubbles[i].GetProperty("panelOrder").GetInt32());
            var eb = expBubbles[i].GetProperty("bbox").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            for (var k = 0; k < 4; k++) result.Bubbles[i].Bbox[k].Should().BeCloseTo(eb[k], 1);
        }
    }

    // Trang thực tế hơn: viền xám (scan nhạt), bong bóng có đuôi + chữ, khung caption chữ nhật có chữ
    private static byte[] RealisticPage(Scalar border)
    {
        using var img = new Mat(600, 900, MatType.CV_8UC3, Scalar.White);
        Cv2.Rectangle(img, new Rect(20, 20, 420, 260), border, 4);
        Cv2.Rectangle(img, new Rect(460, 20, 420, 260), border, 4);
        Cv2.Rectangle(img, new Rect(20, 320, 860, 260), border, 4);
        // Bong bóng có đuôi: elip + tam giác dính liền, viền đen, ruột trắng, có chữ
        Cv2.FillPoly(img, [[new Point(140, 170), new Point(180, 170), new Point(120, 250)]], Scalar.Black);
        Cv2.Ellipse(img, new Point(170, 130), new Size(90, 55), 0, 0, 360, Scalar.Black, -1);
        Cv2.FillPoly(img, [[new Point(146, 168), new Point(174, 168), new Point(126, 238)]], Scalar.White);
        Cv2.Ellipse(img, new Point(170, 130), new Size(87, 52), 0, 0, 360, Scalar.White, -1);
        Cv2.PutText(img, "HELLO", new Point(120, 138), HersheyFonts.HersheySimplex, 1, Scalar.Black, 2);
        // Caption chữ nhật trong khung dưới
        Cv2.Rectangle(img, new Rect(60, 350, 260, 70), Scalar.Black, 2);
        Cv2.PutText(img, "MEANWHILE", new Point(80, 395), HersheyFonts.HersheySimplex, 1, Scalar.Black, 2);
        Cv2.ImEncode(".png", img, out var png);
        return png;
    }

    [Fact]
    public async Task FindsTailedBubbleAndCaptionBox()
    {
        var result = await Split(RealisticPage(Scalar.Black), rtl: false);

        result.Panels.Should().HaveCount(3);
        result.Bubbles.Select(b => b.PanelOrder).Should().Equal(1, 3);
        result.Bubbles[0].Bbox[0].Should().BeInRange(70, 90); // bong bóng elip, x ≈ 170-87
    }

    [Fact]
    public async Task FindsPanelsWithLightGrayBorders()
    {
        var result = await Split(RealisticPage(new Scalar(150, 150, 150)), rtl: false);

        result.Panels.Select(p => p.Bbox[0]).Should().Equal(18, 458, 18);
    }

    [Fact]
    public async Task RectangularPanels_HaveFourCornerPolygon()
    {
        var result = await Split(SyntheticPage(), rtl: false);

        result.Panels.Should().OnlyContain(p => p.Polygon != null && p.Polygon.Length == 4);
    }

    // Hai khung hình thang tách nhau bởi một rãnh trắng chéo (mỗi khung một viền riêng)
    private static byte[] DiagonalGutterPage()
    {
        using var img = new Mat(600, 900, MatType.CV_8UC3, Scalar.White);
        Cv2.Polylines(img, [[new Point(20, 20), new Point(500, 20), new Point(380, 580), new Point(20, 580)]], true, Scalar.Black, 4);
        Cv2.Polylines(img, [[new Point(540, 20), new Point(880, 20), new Point(880, 580), new Point(420, 580)]], true, Scalar.Black, 4);
        Cv2.ImEncode(".png", img, out var png);
        return png;
    }

    [Fact]
    public async Task DiagonalGutter_ReturnsTwoSlantedPolygons()
    {
        var result = await Split(DiagonalGutterPage(), rtl: false);

        result.Panels.Should().HaveCount(2);
        foreach (var p in result.Panels)
        {
            p.Polygon.Should().NotBeNull().And.HaveCount(4);
            // Có cạnh xiên: không phải mọi đỉnh đều nằm trên 2 giá trị x của hộp bao
            p.Polygon!.Select(v => v[0]).Distinct().Count().Should().BeGreaterThan(2);
        }
    }

    // Một khung chữ nhật bị nét chéo chia đôi: 2 khung dùng chung viền → một contour ngoài, hai "lỗ"
    private static byte[] SharedDiagonalBorderPage()
    {
        using var img = new Mat(600, 900, MatType.CV_8UC3, Scalar.White);
        Cv2.Rectangle(img, new Rect(20, 20, 860, 560), Scalar.Black, 4);
        Cv2.Line(img, new Point(560, 20), new Point(340, 580), Scalar.Black, 4);
        Cv2.ImEncode(".png", img, out var png);
        return png;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedDiagonalBorder_SplitsIntoTwoPanels_InReadingOrder(bool rtl)
    {
        var result = await Split(SharedDiagonalBorderPage(), rtl);

        result.Panels.Should().HaveCount(2);
        result.Panels.Should().OnlyContain(p => p.Polygon != null && p.Polygon.Length == 4);
        var firstIsLeft = result.Panels[0].Bbox[0] < result.Panels[1].Bbox[0];
        firstIsLeft.Should().Be(!rtl);
    }

    [Fact]
    public async Task EmptyOrNonImageBytes_ThrowInvalidImageException()
    {
        await FluentActions.Awaiting(() => Split([], rtl: false)).Should().ThrowAsync<InvalidImageException>();
        await FluentActions.Awaiting(() => Split("not an image"u8.ToArray(), rtl: false))
            .Should().ThrowAsync<InvalidImageException>();
    }
}
