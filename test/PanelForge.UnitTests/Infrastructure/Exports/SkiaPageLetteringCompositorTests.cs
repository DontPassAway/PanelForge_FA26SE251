using FluentAssertions;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Domain.Entities.Content;
using PanelForge.Infrastructure.Exports;
using SkiaSharp;
using Xunit;

namespace PanelForge.UnitTests.Infrastructure.Exports;

/// <summary>Bản xuất vẽ lớp chữ theo cùng quy tắc với bàn Letterer / màn duyệt (FE balloonRender.ts).</summary>
public class SkiaPageLetteringCompositorTests
{
    private readonly SkiaPageLetteringCompositor _sut = new();

    /// <summary>Ảnh trang giả: nền xanh đồng màu để biết chỗ nào bị vẽ đè.</summary>
    private static ExportPageImage BluePage(int w = 600, int h = 900)
    {
        using var bmp = new SKBitmap(w, h);
        bmp.Erase(new SKColor(0, 0, 255));
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return new ExportPageImage(1, data.ToArray(), "image/png");
    }

    private static SKColor Pixel(ExportPageImage page, int x, int y)
    {
        using var bmp = SKBitmap.Decode(page.Content);
        return bmp.GetPixel(x, y);
    }

    // Bóng 400×300 ở (200,200) trên khổ 1200×1800; ảnh 600×900 → toạ độ ảnh = toạ độ trang / 2
    private static ExportLetteringElement Balloon(string text = "", TextStyle? style = null, string type = "SPEECH_BALLOON") =>
        new(type, 200, 200, 400, 300, 0, 1, text, style);

    [Fact]
    public void NoText_ReturnsPageUnchanged()
    {
        var page = BluePage();
        _sut.Compose(page, 1200, 1800, [Balloon("   ")]).Should().BeSameAs(page);
    }

    [Fact]
    public void DefaultSpeech_DrawsWhiteOvalInsideBox_ScaledToImage()
    {
        var result = _sut.Compose(BluePage(), 1200, 1800, [Balloon(".")]);
        result.ContentType.Should().Be("image/png");
        // Gần mép trong oval (không có chữ) → nền trắng; góc hộp nằm ngoài oval → giữ nền xanh
        Pixel(result, (200 + 30) / 2, (200 + 150) / 2).Should().Be(SKColors.White);
        Pixel(result, (200 + 5) / 2, (200 + 5) / 2).Should().Be(new SKColor(0, 0, 255));
    }

    [Fact]
    public void TextOnly_TransparentNoBorder_KeepsArtworkAndWhiteTextWithBlackStroke()
    {
        var style = new TextStyle("Mali", 60, Color: "#FFFFFF", StrokeColor: "#000000", StrokeWidth: 3,
            BalloonShape: "none", FillOpacity: 0, BorderWidth: 0);
        var result = _sut.Compose(BluePage(), 1200, 1800, [Balloon("MMMM", style)]);

        // Không có oval trắng che: điểm gần mép bóng vẫn là nền xanh
        Pixel(result, (200 + 30) / 2, (200 + 150) / 2).Should().Be(new SKColor(0, 0, 255));
        // Có cả chữ trắng và viền đen trong vùng chữ
        using var bmp = SKBitmap.Decode(result.Content);
        var colors = Enumerable.Range(100, 200).SelectMany(x => Enumerable.Range(150, 75).Select(y => bmp.GetPixel(x, y))).ToList();
        colors.Should().Contain(SKColors.White);
        colors.Should().Contain(c => c.Red < 40 && c.Green < 40 && c.Blue < 40);
    }

    [Fact]
    public void SemiTransparentFill_BlendsWithArtwork()
    {
        var style = new TextStyle("Mali", 26, BalloonShape: "box", FillColor: "#FFFFFF", FillOpacity: 0.5, BorderWidth: 0);
        var c = Pixel(_sut.Compose(BluePage(), 1200, 1800, [Balloon(".", style)]), (200 + 10) / 2, (200 + 10) / 2);
        c.Red.Should().BeInRange(110, 145);
        c.Blue.Should().Be(255);
    }

    [Theory]
    [InlineData("SPEECH_BALLOON", "oval")]
    [InlineData("THOUGHT_BALLOON", "cloud")]
    [InlineData("NARRATION_BOX", "box")]
    [InlineData("SOUND_EFFECT", "spiky")]
    public void DefaultShape_FollowsType_AndSavedShapeOverridesIt(string type, string expected)
    {
        SkiaPageLetteringCompositor.ResolveAppearance(type, null).Shape.Should().Be(expected);
        SkiaPageLetteringCompositor.ResolveAppearance(type, new TextStyle("Mali", 26, BalloonShape: "none")).Shape
            .Should().Be("none");
    }

    [Fact]
    public void Layout_KeepsChosenSize_ManualLineBreaks_VietnameseWords_AndFlagsOverflow()
    {
        float Measure(string s) => s.EnumerateRunes().Count() * 10f;
        var style = new TextStyle("Mali", 20, LineHeight: 1.3);

        SkiaPageLetteringCompositor.WrapLines("Đêm thứ tư\nliên tiếp", 1000, Measure)
            .Should().Equal("Đêm thứ tư", "liên tiếp");
        SkiaPageLetteringCompositor.WrapLines("Mọi người tập trung", 100, Measure)
            .Should().Equal("Mọi người", "tập trung");
        SkiaPageLetteringCompositor.WrapLines("aaaaaaaaaaaa", 50, Measure).Should().Equal("aaaaa", "aaaaa", "aa");

        var l = SkiaPageLetteringCompositor.Layout(string.Join(' ', Enumerable.Repeat("chữ", 60)), style, "oval", 200, 100, Measure);
        l.Overflow.Should().BeTrue();
        l.LineHeight.Should().Be(26); // 20px × 1.3, không tự thu nhỏ
    }

    [Theory]
    [InlineData("Mali-Regular.ttf", "Mali")]
    [InlineData("Mali-BoldItalic.ttf", "Mali")]
    [InlineData("PatrickHand-Regular.ttf", "Patrick Hand")]
    [InlineData("BeVietnamPro-Italic.ttf", "Be Vietnam Pro")]
    [InlineData("Bangers-Regular.ttf", "Bangers")]
    public void ExportFonts_AreCopiedToOutput_AndMatchFamily(string file, string family)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Exports", "Fonts", file);
        File.Exists(path).Should().BeTrue();
        using var tf = SKTypeface.FromFile(path);
        tf!.FamilyName.Should().Be(family);
    }

    [Fact]
    public void SafeArea_OfOval_StaysInsideEllipse()
    {
        var (x, y, w, h) = SkiaPageLetteringCompositor.SafeArea("oval", 300, 200);
        double E(double px, double py) => Math.Pow((px - 150) / 150, 2) + Math.Pow((py - 100) / 100, 2);
        E(x, y).Should().BeLessThanOrEqualTo(1);
        E(x + w, y + h).Should().BeLessThanOrEqualTo(1);
        SkiaPageLetteringCompositor.SafeArea("box", 300, 200).Should().Be((12d, 12d, 276d, 176d));
    }
}
