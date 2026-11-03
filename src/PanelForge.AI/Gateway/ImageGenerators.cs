using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using PanelForge.AI.Contracts;

namespace PanelForge.AI.Gateway;

/// <summary>Cấu hình sinh ảnh (section "ImageGen"). Token Cloudflare đặt qua user-secrets/biến môi trường, không đưa vào appsettings.json.</summary>
public sealed class ImageGenOptions
{
    public const string SectionName = "ImageGen";

    public string GeminiBaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/models/";
    public string CloudflareBaseUrl { get; set; } = "https://api.cloudflare.com/client/v4/accounts/";
    public string? CloudflareAccountId { get; set; }
    public string? CloudflareApiToken { get; set; }
    /// <summary>Số bước khử nhiễu cho model Cloudflare (FLUX schnell tối đa 8).</summary>
    public int CloudflareSteps { get; set; } = 4;
    public int TimeoutSeconds { get; set; } = 90;
}

/// <summary>Một nhà cung cấp sinh ảnh. Thêm provider mới = thêm một class + một dòng trong <see cref="ImageGeneratorFactory"/>.</summary>
public interface IImageGenerator
{
    Task<ImageGenResponse> GenerateAsync(ImageGenRequest req, CancellationToken ct);
}

public sealed class ImageGeneratorFactory(IHttpClientFactory http, IOptions<ImageGenOptions> options)
{
    public const string HttpClientName = "image-gen";

    public IImageGenerator? Create(string provider) => provider switch
    {
        ImageProviders.Gemini => new GeminiImageGenerator(http.CreateClient(HttpClientName), options.Value),
        ImageProviders.Cloudflare => new CloudflareImageGenerator(http.CreateClient(HttpClientName), options.Value),
        ImageProviders.Mock => new MockImageGenerator(),
        _ => null
    };
}

internal static class ImageGen
{
    public static ImageGenResponse Fail(DateTime started, string code, string message, bool safety = false) =>
        new(false, null, null, safety, null, null, started, DateTime.UtcNow, code, message);

    public static ImageGenResponse Ok(DateTime started, byte[] bytes, string mime, long? promptTokens = null, long? outTokens = null) =>
        new(true, Convert.ToBase64String(bytes), mime, false, promptTokens, outTokens, started, DateTime.UtcNow, null, null);

    /// <summary>Không retry: mỗi ảnh tốn quota. 429/5xx báo người dùng thử lại sau.</summary>
    public static string HttpError(HttpStatusCode status) => (int)status switch
    {
        401 or 403 => "Key không hợp lệ hoặc không có quyền dùng model ảnh này.",
        404 => "Model ảnh không tồn tại (kiểm tra tên model trong cấu hình).",
        429 => "Nhà cung cấp ảnh đang giới hạn lượt gọi (hết quota free?). Thử lại sau.",
        >= 500 => "Nhà cung cấp ảnh đang lỗi. Thử lại sau.",
        _ => $"Nhà cung cấp ảnh từ chối yêu cầu (HTTP {(int)status})."
    };
}

/// <summary>
/// Gemini generateContent với responseModalities IMAGE. Gửi kèm ảnh character sheet (inline_data) để giữ nhân vật nhất quán.
/// Bị chặn an toàn (promptFeedback.blockReason / finishReason SAFETY…) → SafetyBlocked, không retry.
/// </summary>
public sealed class GeminiImageGenerator(HttpClient http, ImageGenOptions options) : IImageGenerator
{
    public async Task<ImageGenResponse> GenerateAsync(ImageGenRequest req, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        var parts = new JsonArray();
        foreach (var r in req.References)
        {
            parts.Add(new JsonObject { ["text"] = $"Ảnh tham chiếu nhân vật: {r.Label}. Giữ đúng khuôn mặt, tóc, trang phục." });
            parts.Add(new JsonObject { ["inline_data"] = new JsonObject { ["mime_type"] = r.MimeType, ["data"] = r.Base64 } });
        }
        parts.Add(new JsonObject { ["text"] = req.Prompt });
        var body = new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject { ["role"] = "user", ["parts"] = parts }),
            ["generationConfig"] = new JsonObject { ["responseModalities"] = new JsonArray("TEXT", "IMAGE") }
        };

        using var msg = new HttpRequestMessage(HttpMethod.Post,
            $"{options.GeminiBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(req.Model)}:generateContent")
        {
            Content = JsonContent.Create(body)
        };
        msg.Headers.Add("x-goog-api-key", req.ApiKey);
        using var res = await http.SendAsync(msg, ct);
        if (!res.IsSuccessStatusCode) return ImageGen.Fail(started, "PROVIDER_ERROR", ImageGen.HttpError(res.StatusCode));

        var json = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct));
        long? inTok = json?["usageMetadata"]?["promptTokenCount"]?.GetValue<long>();
        long? outTok = json?["usageMetadata"]?["candidatesTokenCount"]?.GetValue<long>();
        if (json?["promptFeedback"]?["blockReason"]?.GetValue<string>() is { } block)
            return ImageGen.Fail(started, "SAFETY_BLOCKED", $"Gemini chặn prompt ({block}). Sửa prompt rồi sinh lại.", safety: true);

        var candidate = json?["candidates"]?[0];
        foreach (var part in candidate?["content"]?["parts"]?.AsArray() ?? [])
        {
            var inline = part?["inlineData"] ?? part?["inline_data"];
            if (inline?["data"]?.GetValue<string>() is { Length: > 0 } data)
                return ImageGen.Ok(started, Convert.FromBase64String(data),
                    (inline["mimeType"] ?? inline["mime_type"])?.GetValue<string>() ?? "image/png", inTok, outTok);
        }

        var finish = candidate?["finishReason"]?.GetValue<string>() ?? "";
        return finish.Contains("SAFETY") || finish.Contains("PROHIBITED") || finish.Contains("BLOCK")
            ? ImageGen.Fail(started, "SAFETY_BLOCKED", $"Gemini chặn ảnh ({finish}). Sửa prompt rồi sinh lại.", safety: true)
            : ImageGen.Fail(started, "NO_IMAGE", $"Gemini không trả ảnh (finishReason {(finish.Length > 0 ? finish : "trống")}). Model có hỗ trợ sinh ảnh không?");
    }
}

/// <summary>
/// Cloudflare Workers AI text-to-image (vd. @cf/black-forest-labs/flux-1-schnell). Không nhận ảnh tham chiếu: chỉ dùng prompt
/// (ArtistAgent đã chèn mô tả ngoại hình từ Bible vào prompt). FLUX trả JSON {result:{image}}, SDXL trả thẳng byte ảnh.
/// </summary>
public sealed class CloudflareImageGenerator(HttpClient http, ImageGenOptions options) : IImageGenerator
{
    public async Task<ImageGenResponse> GenerateAsync(ImageGenRequest req, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(options.CloudflareAccountId) || string.IsNullOrWhiteSpace(options.CloudflareApiToken))
            return ImageGen.Fail(started, "AI_UNAVAILABLE", "Chưa cấu hình ImageGen:CloudflareAccountId / CloudflareApiToken cho AI service.");

        // Tên model dạng @cf/vendor/name: giữ dấu '/', chỉ escape từng đoạn
        var model = string.Join('/', req.Model.Split('/').Select(Uri.EscapeDataString));
        using var msg = new HttpRequestMessage(HttpMethod.Post,
            $"{options.CloudflareBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(options.CloudflareAccountId)}/ai/run/{model}")
        {
            Content = JsonContent.Create(new { prompt = req.Prompt, steps = Math.Clamp(options.CloudflareSteps, 1, 8) })
        };
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.CloudflareApiToken);
        using var res = await http.SendAsync(msg, ct);

        var mime = res.Content.Headers.ContentType?.MediaType ?? "";
        if (res.IsSuccessStatusCode && mime.StartsWith("image/"))
            return ImageGen.Ok(started, await res.Content.ReadAsByteArrayAsync(ct), mime);

        var text = await res.Content.ReadAsStringAsync(ct);
        JsonNode? json = null;
        try { json = JsonNode.Parse(text); } catch (JsonException) { }
        if (res.IsSuccessStatusCode && json?["result"]?["image"]?.GetValue<string>() is { Length: > 0 } b64)
        {
            var bytes = Convert.FromBase64String(b64);
            return ImageGen.Ok(started, bytes, bytes is [0x89, 0x50, ..] ? "image/png" : "image/jpeg");
        }

        var error = json?["errors"]?[0]?["message"]?.GetValue<string>() ?? "";
        return error.Contains("NSFW", StringComparison.OrdinalIgnoreCase) || error.Contains("safety", StringComparison.OrdinalIgnoreCase)
            ? ImageGen.Fail(started, "SAFETY_BLOCKED", "Cloudflare chặn nội dung prompt. Sửa prompt rồi sinh lại.", safety: true)
            : ImageGen.Fail(started, "PROVIDER_ERROR", res.IsSuccessStatusCode
                ? "Cloudflare không trả ảnh."
                : $"{ImageGen.HttpError(res.StatusCode)} {(error.Length > 200 ? error[..200] : error)}".Trim());
    }
}

/// <summary>Chế độ Mock (dev/test, không gọi mạng): trả ảnh PNG 1×1.</summary>
public sealed class MockImageGenerator : IImageGenerator
{
    public static readonly byte[] Png1x1 = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    public Task<ImageGenResponse> GenerateAsync(ImageGenRequest req, CancellationToken ct) =>
        Task.FromResult(ImageGen.Ok(DateTime.UtcNow, Png1x1, "image/png"));
}
