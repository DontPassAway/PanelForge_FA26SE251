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

    [Fact]
    public async Task EmptyOrNonImageBytes_ThrowInvalidImageException()
    {
        await FluentActions.Awaiting(() => Split([], rtl: false)).Should().ThrowAsync<InvalidImageException>();
        await FluentActions.Awaiting(() => Split("not an image"u8.ToArray(), rtl: false))
            .Should().ThrowAsync<InvalidImageException>();
    }
}
