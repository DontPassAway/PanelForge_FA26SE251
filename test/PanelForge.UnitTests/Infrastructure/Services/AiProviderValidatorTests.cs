using System.Net;
using FluentAssertions;
using Moq;
using PanelForge.Infrastructure.Services;
using Xunit;

namespace PanelForge.UnitTests.Infrastructure.Services;

public class AiProviderValidatorTests
{
    private sealed class Handler(HttpStatusCode status) : HttpMessageHandler
    {
        public string? Key { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Key = request.Headers.GetValues("x-goog-api-key").Single();
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    private static AiProviderValidator Create(Handler handler) =>
        new(Mock.Of<IHttpClientFactory>(f => f.CreateClient(It.IsAny<string>()) == new HttpClient(handler)));

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    public async Task Gemini_UsesGoogleAnswer(HttpStatusCode status, bool ok)
    {
        var handler = new Handler(status);
        (await Create(handler).ValidateCredentialsAsync("Google Gemini", " key-123 ")).IsSuccess.Should().Be(ok);
        handler.Key.Should().Be("key-123");
    }

    [Fact]
    public async Task CloudflareModels_KeepsTextAndImageOnly_AndMarksCfModelsFree()
    {
        const string body = """
            {"success":true,"result":[
              {"name":"@cf/meta/llama-3.1-8b-instruct","task":{"name":"Text Generation"},"description":"small"},
              {"name":"pruna/z-image-turbo","task":{"name":"Text-to-Image"}},
              {"name":"@cf/black-forest-labs/flux-1-schnell","task":{"name":"Text-to-Image"}},
              {"name":"@cf/openai/whisper","task":{"name":"Automatic Speech Recognition"}}
            ]}
            """;
        HttpRequestMessage? sent = null;
        var http = new HttpClient(new StubHandler(r =>
        {
            sent = r;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }));
        var validator = new AiProviderValidator(Mock.Of<IHttpClientFactory>(f => f.CreateClient(It.IsAny<string>()) == http));

        var result = await validator.ListCloudflareModelsAsync("acc-1", "cf-token");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Select(m => (m.Id, m.Kind, m.Free)).Should().Equal(
            ("@cf/black-forest-labs/flux-1-schnell", "image", true),
            ("pruna/z-image-turbo", "image", false),
            ("@cf/meta/llama-3.1-8b-instruct", "text", true));
        sent!.RequestUri!.AbsolutePath.Should().Be("/client/v4/accounts/acc-1/ai/models/search");
        sent.Headers.Authorization!.Parameter.Should().Be("cf-token");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(f(r));
    }

    [Fact]
    public async Task OtherProviders_AreRejected_WithoutCallingNetwork()
    {
        var handler = new Handler(HttpStatusCode.OK);
        (await Create(handler).ValidateCredentialsAsync("OpenAI", "sk-1234567890")).IsSuccess.Should().BeFalse();
        handler.Key.Should().BeNull();
    }
}
