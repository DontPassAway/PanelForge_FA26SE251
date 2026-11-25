using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using PanelForge.AI.Contracts;
using PanelForge.AI.Gateway;
using Xunit;

namespace PanelForge.UnitTests.Ai;

/// <summary>Cloudflare: model @cf/ gọi /ai/run/{model} và nhận base64; model đối tác (pruna/z-image-turbo) gọi /ai/run và nhận presigned URL.</summary>
public class CloudflareImageGeneratorTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    private sealed class Handler(Func<HttpRequestMessage, string?, HttpResponseMessage> f) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Req, string? Body)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.Content is null ? null : await r.Content.ReadAsStringAsync(ct);
            Calls.Add((r, body));
            return f(r, body);
        }
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static ImageGenRequest Req(string model) =>
        new(ImageProviders.Cloudflare, model, "cf-token", "a fox", [], AccountId: "acc-1");

    [Fact]
    public async Task ThirdPartyModel_PostsModelInBody_ThenDownloadsPresignedUrl_WithoutToken()
    {
        var handler = new Handler((r, _) => r.Method == HttpMethod.Post
            ? Json("""{"state":"Completed","result":{"image":"https://examples.aig.cloudflare.com/x.png"}}""")
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) });
        var gen = new CloudflareImageGenerator(new HttpClient(handler), new ImageGenOptions());

        var res = await gen.GenerateAsync(Req("pruna/z-image-turbo"), default);

        res.Succeeded.Should().BeTrue();
        Convert.FromBase64String(res.ImageBase64!).Should().Equal(Png);
        res.MimeType.Should().Be("image/png");
        var (post, body) = handler.Calls[0];
        post.RequestUri!.ToString().Should().Be("https://api.cloudflare.com/client/v4/accounts/acc-1/ai/run");
        post.Headers.Authorization!.Parameter.Should().Be("cf-token");
        var json = JsonNode.Parse(body!)!;
        json["model"]!.GetValue<string>().Should().Be("pruna/z-image-turbo");
        json["input"]!["prompt"]!.GetValue<string>().Should().Be("a fox");
        var download = handler.Calls[1].Req;
        download.RequestUri!.Host.Should().Be("examples.aig.cloudflare.com");
        download.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task ThirdPartyModel_RejectsNonHttpsImageUrl()
    {
        var handler = new Handler((_, _) => Json("""{"result":{"image":"http://169.254.169.254/latest"}}"""));
        var gen = new CloudflareImageGenerator(new HttpClient(handler), new ImageGenOptions());

        var res = await gen.GenerateAsync(Req("pruna/z-image-turbo"), default);

        res.Succeeded.Should().BeFalse();
        handler.Calls.Should().HaveCount(1);
    }

    [Fact]
    public async Task CfModel_KeepsModelInPath_AndDecodesBase64()
    {
        var handler = new Handler((_, _) => Json($$$"""{"result":{"image":"{{{Convert.ToBase64String(Png)}}}"}}"""));
        var gen = new CloudflareImageGenerator(new HttpClient(handler), new ImageGenOptions());

        var res = await gen.GenerateAsync(Req("@cf/black-forest-labs/flux-1-schnell"), default);

        res.Succeeded.Should().BeTrue();
        handler.Calls.Single().Req.RequestUri!.AbsolutePath
            .Should().EndWith("/accounts/acc-1/ai/run/%40cf/black-forest-labs/flux-1-schnell");
    }
}
