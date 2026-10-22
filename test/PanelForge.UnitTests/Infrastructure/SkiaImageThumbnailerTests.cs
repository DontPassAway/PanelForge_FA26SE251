using FluentAssertions;
using PanelForge.Infrastructure.Exports;
using SkiaSharp;
using Xunit;

namespace PanelForge.UnitTests.Infrastructure;

public class SkiaImageThumbnailerTests
{
    private static MemoryStream Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Coral);
        using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        return new MemoryStream(data.ToArray());
    }

    [Fact]
    public void LargeImage_IsShrunkToMaxWidth_AsWebp_KeepingAspectRatio()
    {
        var bytes = new SkiaImageThumbnailer().CreateWebp(Png(1200, 1800), 400);

        using var thumb = SKBitmap.Decode(bytes);
        thumb.Width.Should().Be(400);
        thumb.Height.Should().Be(600);
        SKCodec.Create(new MemoryStream(bytes!)).EncodedFormat.Should().Be(SKEncodedImageFormat.Webp);
    }

    [Fact]
    public void SmallImage_IsNotUpscaled() =>
        SKBitmap.Decode(new SkiaImageThumbnailer().CreateWebp(Png(200, 100), 400)).Width.Should().Be(200);

    [Fact]
    public void NonImage_ReturnsNull() =>
        new SkiaImageThumbnailer().CreateWebp(new MemoryStream([1, 2, 3]), 400).Should().BeNull();
}
