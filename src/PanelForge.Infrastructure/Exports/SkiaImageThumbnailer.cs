using PanelForge.Application.Common.Interfaces.Services;
using SkiaSharp;

namespace PanelForge.Infrastructure.Exports;

public sealed class SkiaImageThumbnailer : IImageThumbnailer
{
    public byte[]? CreateWebp(Stream source, int maxWidth)
    {
        using var original = SKBitmap.Decode(source);
        if (original is null) return null;

        var width = Math.Min(maxWidth, original.Width);
        var height = Math.Max(1, (int)Math.Round(original.Height * (double)width / original.Width));
        using var resized = original.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
        using var image = SKImage.FromBitmap(resized ?? original);
        using var data = image.Encode(SKEncodedImageFormat.Webp, 80);
        return data?.ToArray();
    }
}
