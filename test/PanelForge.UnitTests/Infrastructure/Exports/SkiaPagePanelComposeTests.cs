using FluentAssertions;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Infrastructure.Exports;
using SkiaSharp;
using Xunit;

namespace PanelForge.UnitTests.Infrastructure.Exports;

/// <summary>Ghép trang từ ảnh từng Panel: cùng quy tắc FE compositePageRenderer (contain, clip theo khung, viền).</summary>
public class SkiaPagePanelComposeTests
{
    private readonly SkiaPageLetteringCompositor _sut = new();

    private static byte[] Solid(SKColor color, int w = 100, int h = 100)
    {
        using var bmp = new SKBitmap(w, h);
        bmp.Erase(color);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static SKColor Pixel(ExportPageImage page, int x, int y)
    {
        using var bmp = SKBitmap.Decode(page.Content);
        return bmp.GetPixel(x, y);
    }

    [Fact]
    public void ComposePanels_PlacesEachImageInsideItsFrame_OnAWhitePage()
    {
        var red = new SKColor(255, 0, 0);
        var green = new SKColor(0, 255, 0);

        var page = _sut.ComposePanels(3, 1200, 1800,
        [
            new PagePanelLayer(Solid(red), 60, 60, 1080, 600),
            new PagePanelLayer(Solid(green), 60, 720, 528, 516, Borderless: true),
            new PagePanelLayer(null, 612, 720, 528, 516) // khung chưa có ảnh: chỉ vẽ viền
        ]);

        page.PageNumber.Should().Be(3);
        using (var bmp = SKBitmap.Decode(page.Content))
        {
            bmp.Width.Should().Be(1200);
            bmp.Height.Should().Be(1800);
        }
        // Ảnh vuông 100×100 co "contain" vào khung 1080×600 → phủ giữa khung, hai bên là nền trắng
        Pixel(page, 600, 360).Should().Be(red);
        Pixel(page, 100, 360).Should().Be(SKColors.White);
        Pixel(page, 324, 978).Should().Be(green);
        Pixel(page, 600, 1500).Should().Be(SKColors.White);   // ngoài mọi khung
        Pixel(page, 876, 720).Red.Should().BeLessThan(60);    // viền khung trống (#171717)
        Pixel(page, 876, 900).Should().Be(SKColors.White);    // trong khung trống
    }

    [Fact]
    public void ComposePanels_ClipsToPolygonCorners_AndUnreadableImageBecomesPlaceholder()
    {
        var blue = new SKColor(0, 0, 255);
        // Tam giác: nửa trên-trái của khung
        var page = _sut.ComposePanels(1, 400, 400,
        [
            new PagePanelLayer(Solid(blue), 0, 0, 400, 400, [(0, 0), (1, 0), (0, 1)], Borderless: true),
            new PagePanelLayer([1, 2, 3], 0, 0, 0, 0)
        ]);

        Pixel(page, 50, 50).Should().Be(blue);
        Pixel(page, 350, 350).Should().Be(SKColors.White);
    }

    [Theory]
    [InlineData("""{"x":612,"y":642,"unit":"px","width":528,"height":516,"pageWidthPx":1200,"pageHeightPx":1800}""", 612, 642, 528, 516)]
    [InlineData("""{"normalized":{"x":0.5,"y":0.25,"w":0.25,"h":0.5},"pageWidthPx":1200,"pageHeightPx":1800}""", 600, 450, 300, 900)]
    [InlineData("""{"x":0.1,"y":0.1,"width":0.5,"height":0.5}""", 120, 180, 600, 900)]
    [InlineData("""{"x":300,"y":450,"width":300,"height":450,"pageWidthPx":600,"pageHeightPx":900}""", 600, 900, 600, 900)]
    public void PanelBox_Parse_MatchesFrontendRules(string json, double x, double y, double w, double h)
    {
        var box = PanelBox.Parse(json, 1200, 1800);

        box.X.Should().BeApproximately(x, 0.5);
        box.Y.Should().BeApproximately(y, 0.5);
        box.Width.Should().BeApproximately(w, 0.5);
        box.Height.Should().BeApproximately(h, 0.5);
    }
}
