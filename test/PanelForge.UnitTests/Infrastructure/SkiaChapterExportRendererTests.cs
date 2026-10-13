using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Exports;
using SkiaSharp;
using Xunit;

namespace PanelForge.UnitTests.Infrastructure;

/// <summary>UC-12: CBZ/ảnh trang/PDF/webtoon dựng đúng thứ tự trang, đúng hướng đọc, báo lỗi rõ khi ảnh hỏng.</summary>
public class SkiaChapterExportRendererTests
{
    private readonly SkiaChapterExportRenderer _renderer = new();

    private static ExportPageImage Page(int number, int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(SKColors.Gray);
        using var image = surface.Snapshot();
        using var data = image.Encode(format, 90);
        var type = format switch { SKEncodedImageFormat.Jpeg => "image/jpeg", SKEncodedImageFormat.Webp => "image/webp", _ => "image/png" };
        return new ExportPageImage(number, data.ToArray(), type);
    }

    private static ExportManifest Manifest(ReadingDirection direction = ReadingDirection.RightToLeft, string config = "{}") =>
        new("Đèn biển", 1.5m, "Lá thư", direction, config);

    [Fact]
    public void Cbz_KeepsPageOrder_ConvertsWebp_AndWritesRightToLeftComicInfo()
    {
        var file = _renderer.Render("CBZ", Manifest(),
            [Page(1, 20, 30), Page(2, 20, 30, SKEncodedImageFormat.Jpeg), Page(3, 20, 30, SKEncodedImageFormat.Webp)]);

        file.Extension.Should().Be("cbz");
        using var zip = new ZipArchive(new MemoryStream(file.Content));
        zip.Entries.Select(e => e.FullName).Should().Equal("001.png", "002.jpg", "003.png", "ComicInfo.xml");
        using var reader = new StreamReader(zip.GetEntry("ComicInfo.xml")!.Open(), Encoding.UTF8);
        var xml = reader.ReadToEnd();
        xml.Should().Contain("<Manga>YesAndRightToLeft</Manga>").And.Contain("<Series>Đèn biển</Series>")
            .And.Contain("<Number>1.5</Number>").And.Contain("<PageCount>3</PageCount>");
    }

    [Fact]
    public void PageImages_IsZipWithoutComicInfo_AndLeftToRightSeriesIsNotManga()
    {
        using var cbz = new ZipArchive(new MemoryStream(_renderer.Render("CBZ", Manifest(ReadingDirection.LeftToRight), [Page(1, 10, 10)]).Content));
        new StreamReader(cbz.GetEntry("ComicInfo.xml")!.Open()).ReadToEnd().Should().Contain("<Manga>No</Manga>");

        using var zip = new ZipArchive(new MemoryStream(_renderer.Render("PAGE_IMAGES", Manifest(), [Page(1, 10, 10)]).Content));
        zip.Entries.Select(e => e.FullName).Should().Equal("001.png");
    }

    [Fact]
    public void Pdf_HasOnePagePerImage()
    {
        var file = _renderer.Render("PDF", Manifest(config: """{"resolution":"300dpi"}"""), [Page(1, 30, 45), Page(2, 45, 30)]);

        file.ContentType.Should().Be("application/pdf");
        var text = Encoding.Latin1.GetString(file.Content);
        text.Should().StartWith("%PDF");
        Regex.Matches(text, @"/Type\s*/Page\b").Count.Should().Be(2);
    }

    [Fact]
    public void Webtoon_ScalesEveryPageToStripWidth_AndStacksThemVertically()
    {
        var file = _renderer.Render("WEBTOON", Manifest(config: """{"stripWidth":800,"quality":80}"""),
            [Page(1, 100, 150), Page(2, 200, 100)]);

        file.ContentType.Should().Be("image/jpeg");
        using var bitmap = SKBitmap.Decode(file.Content);
        bitmap.Width.Should().Be(800);
        bitmap.Height.Should().Be(1200 + 400);
    }

    [Fact]
    public void Webtoon_TooTall_AndBrokenImage_FailWithReadableMessage()
    {
        var tall = Enumerable.Range(1, 60).Select(n => Page(n, 10, 100)).ToList(); // 60 × 8000px ở chiều rộng 800
        _renderer.Invoking(r => r.Render("WEBTOON", Manifest(), tall))
            .Should().Throw<InvalidOperationException>().WithMessage("*quá dài*");

        _renderer.Invoking(r => r.Render("PDF", Manifest(), [new ExportPageImage(7, [1, 2, 3], "image/png")]))
            .Should().Throw<InvalidOperationException>().WithMessage("*trang 7*");
    }

    [Theory]
    [InlineData("cbz", true)]
    [InlineData("WEBTOON", true)]
    [InlineData("OPEN_JSON", false)]
    public void Supports_KnownImageFormatsOnly(string format, bool expected) => _renderer.Supports(format).Should().Be(expected);
}
