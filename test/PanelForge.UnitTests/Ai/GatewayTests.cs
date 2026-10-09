using System.Net;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Moq;
using Polly.CircuitBreaker;
using PanelForge.AI.Contracts;
using PanelForge.AI.Demo;
using PanelForge.AI.Gateway;
using Xunit;

namespace PanelForge.UnitTests.Ai;

public class GatewayTests
{
    private sealed class FailingClient : IChatClient
    {
        public int Calls { get; private set; }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new HttpRequestException("provider down");
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task Gateway_RetriesTransientErrors_ThenOpensCircuit_AndStopsCallingProvider()
    {
        var opts = Options.Create(new ProviderOptions
        {
            MaxRetries = 2, RetryBaseDelaySeconds = 0, MinCallIntervalMs = 0, BreakerMinimumThroughput = 4,
            BreakerFailureRatio = 0.5, BreakerBreakSeconds = 30, CallTimeoutSeconds = 5
        });
        var inner = new FailingClient();
        var gateway = new LlmGateway(inner, new LlmResilience(opts));
        var msgs = new[] { new ChatMessage(ChatRole.User, "hi") };

        var first = async () => await gateway.GetResponseAsync(msgs);
        await first.Should().ThrowAsync<HttpRequestException>();
        inner.Calls.Should().Be(3, "1 lần gọi + 2 lần thử lại");

        // Lần sau: các lỗi liên tiếp làm mạch mở
        var second = async () => await gateway.GetResponseAsync(msgs);
        await second.Should().ThrowAsync<Exception>();
        var callsWhenOpen = inner.Calls;

        var third = async () => await gateway.GetResponseAsync(msgs);
        await third.Should().ThrowAsync<BrokenCircuitException>();
        inner.Calls.Should().Be(callsWhenOpen, "mạch mở: không gọi provider nữa");

        LlmGateway.Classify(new BrokenCircuitException()).Code.Should().Be("PROVIDER_UNAVAILABLE");
        LlmGateway.Classify(new HttpRequestException()).Code.Should().Be("PROVIDER_UNAVAILABLE");
        LlmGateway.Classify(new InvalidOperationException("secret=abc")).Message.Should().NotContain("secret=abc");
    }

    private sealed class FakeHttpFactory(HttpStatusCode status) : IHttpClientFactory
    {
        public HttpRequestMessage? Last { get; private set; }
        public HttpClient CreateClient(string name) => new(new Handler(this, status));
        private sealed class Handler(FakeHttpFactory f, HttpStatusCode s) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            {
                f.Last = r;
                return Task.FromResult(new HttpResponseMessage(s));
            }
        }
    }

    private static ChatClientFactory Factory(HttpStatusCode status, out FakeHttpFactory http)
    {
        var opts = Options.Create(new ProviderOptions());
        http = new FakeHttpFactory(status);
        return new ChatClientFactory(opts, new LlmResilience(opts), http);
    }

    [Fact]
    public void Factory_MockNeedsNoKey_GeminiWithoutKeyIsRefused_NeverFallsBackToMock()
    {
        var f = Factory(HttpStatusCode.OK, out _);

        f.Create(new AiRunJob(Guid.NewGuid(), "Mock", "m", null, 2)).Should().BeOfType<MockChatClient>();
        var noKey = () => f.Create(new AiRunJob(Guid.NewGuid(), "Gemini", "gemini-2.5-flash", null, 2));
        noKey.Should().Throw<InvalidOperationException>();
        f.Create(new AiRunJob(Guid.NewGuid(), "Gemini", "gemini-2.5-flash", "k", 2)).Should().BeOfType<LlmGateway>();
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, null)]
    [InlineData(HttpStatusCode.NotFound, "MODEL_UNAVAILABLE")]
    [InlineData(HttpStatusCode.Forbidden, "PROVIDER_AUTH_FAILED")]
    [InlineData(HttpStatusCode.InternalServerError, "PROVIDER_UNAVAILABLE")]
    public async Task Factory_ProbesGeminiModelAvailability_BeforeRunning(HttpStatusCode status, string? expectedCode)
    {
        var f = Factory(status, out var http);

        var probe = await f.ProbeGeminiModelAsync(new AiRunJob(Guid.NewGuid(), "Gemini", "gemini-2.5-flash", "key-xyz", 2), default);

        probe?.Code.Should().Be(expectedCode);
        if (expectedCode is null) probe.Should().BeNull();
        http.Last!.RequestUri!.AbsolutePath.Should().EndWith("/models/gemini-2.5-flash");
        http.Last.RequestUri.Query.Should().BeEmpty("key đi bằng header, không nằm trong URL/log");
        http.Last.Headers.GetValues("x-goog-api-key").Single().Should().Be("key-xyz");
    }
}