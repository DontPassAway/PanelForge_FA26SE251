using PanelForge.AI.Contracts;

namespace PanelForge.AI.Gateway;

/// <summary>Ảnh giả cho unit test, không gọi mạng.</summary>
public sealed class MockImageGenerator : IImageGenerator
{
    public static readonly byte[] Png1x1 = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    public Task<ImageGenResponse> GenerateAsync(ImageGenRequest req, CancellationToken ct) =>
        Task.FromResult(new ImageGenResponse(true, Convert.ToBase64String(Png1x1), "image/png", false, null, null,
            DateTime.UtcNow, DateTime.UtcNow, null, null));
}
