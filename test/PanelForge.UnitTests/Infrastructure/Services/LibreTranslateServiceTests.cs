using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PanelForge.Infrastructure.Services;
using Xunit;

namespace PanelForge.UnitTests.Infrastructure.Services;

public class LibreTranslateServiceTests
{
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public int CallCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            if (request.Content != null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            return _responseFactory(request);
        }
    }

    [Fact]
    public async Task TranslateAsync_SuccessfulResponse_ReturnsTranslatedText()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { translatedText = "Xin chào thế giới" }),
                System.Text.Encoding.UTF8,
                "application/json")
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        var service = new LibreTranslateService(client, NullLogger<LibreTranslateService>.Instance);

        var result = await service.TranslateAsync("Hello world", "en", "vi");

        result.Should().Be("Xin chào thế giới");
        handler.CallCount.Should().Be(1);
        handler.LastRequest?.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest?.RequestUri?.ToString().Should().EndWith("/translate");
        handler.LastRequestBody.Should().Contain("\"q\":\"Hello world\"");
        handler.LastRequestBody.Should().Contain("\"source\":\"en\"");
        handler.LastRequestBody.Should().Contain("\"target\":\"vi\"");
    }

    [Fact]
    public async Task TranslateAsync_ErrorResponse_ReturnsOriginalTextFallback()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{\"error\":\"Internal translation error\"}", System.Text.Encoding.UTF8, "application/json")
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        var service = new LibreTranslateService(client, NullLogger<LibreTranslateService>.Instance);

        var originalText = "Text with error";
        var result = await service.TranslateAsync(originalText, "en", "vi");

        result.Should().Be(originalText);
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task TranslateAsync_ConnectionError_ReturnsOriginalTextFallback()
    {
        var handler = new MockHttpMessageHandler(_ => throw new HttpRequestException("Connection refused"));

        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        var service = new LibreTranslateService(client, NullLogger<LibreTranslateService>.Instance);

        var originalText = "Text with connection drop";
        var result = await service.TranslateAsync(originalText, "en", "vi");

        result.Should().Be(originalText);
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task TranslateAsync_Timeout_ReturnsOriginalTextFallback()
    {
        var handler = new MockHttpMessageHandler(_ => throw new TaskCanceledException("Request timed out"));

        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        var service = new LibreTranslateService(client, NullLogger<LibreTranslateService>.Instance);

        var originalText = "Text that times out";
        var result = await service.TranslateAsync(originalText, "en", "vi");

        result.Should().Be(originalText);
        handler.CallCount.Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TranslateAsync_EmptyOrWhitespaceText_ReturnsAsIsWithoutCallingHttp(string emptyText)
    {
        var handler = new MockHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called"));
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        var service = new LibreTranslateService(client, NullLogger<LibreTranslateService>.Instance);

        var result = await service.TranslateAsync(emptyText, "en", "vi");

        result.Should().Be(emptyText);
        handler.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("vi", "vi")]
    [InlineData("EN", "en")]
    [InlineData("vi", "VI")]
    public async Task TranslateAsync_SameSourceAndTarget_ReturnsAsIsWithoutCallingHttp(string src, string tgt)
    {
        var handler = new MockHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called"));
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        var service = new LibreTranslateService(client, NullLogger<LibreTranslateService>.Instance);

        var originalText = "No translation needed";
        var result = await service.TranslateAsync(originalText, src, tgt);

        result.Should().Be(originalText);
        handler.CallCount.Should().Be(0);
    }
}
