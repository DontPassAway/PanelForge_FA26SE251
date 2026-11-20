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
    public async Task OtherProviders_AreRejected_WithoutCallingNetwork()
    {
        var handler = new Handler(HttpStatusCode.OK);
        (await Create(handler).ValidateCredentialsAsync("OpenAI", "sk-1234567890")).IsSuccess.Should().BeFalse();
        handler.Key.Should().BeNull();
    }
}
