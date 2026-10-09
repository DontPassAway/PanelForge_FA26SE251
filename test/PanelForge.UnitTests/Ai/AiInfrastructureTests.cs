using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using PanelForge.AI.Contracts;
using PanelForge.API.Common.Attributes;
using PanelForge.Application.Common;
using PanelForge.Application.Features.AiAgents.Models;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.ExternalServices;
using Xunit;

namespace PanelForge.UnitTests.Ai;

public class AiInfrastructureTests
{
    // ── AiClient: AI tắt không ném lỗi, trả AI_UNAVAILABLE ─────────────────────
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpRequestMessage? Last { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Last = request;
            return Task.FromResult(f(request));
        }
    }

    private static AiClient Client(HttpMessageHandler handler, string? key = "secret") =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5055/") },
            new AiAgentSettings { ServiceKey = key }, NullLogger<AiClient>.Instance);

    private static readonly AiRunJob Job = new(Guid.NewGuid(), "Mock", "mock-fixture-v1", null, 2);

    [Fact]
    public async Task AiClient_WhenServiceDown_ReturnsAiUnavailable_InsteadOfThrowing()
    {
        var client = Client(new StubHandler(_ => throw new HttpRequestException("connection refused")));

        var ack = await client.StartRunAsync(Job);

        ack.Accepted.Should().BeFalse();
        ack.ErrorCode.Should().Be(ResultErrorCodes.AiUnavailable);
    }

    [Fact]
    public async Task AiClient_WhenTimeout_OrQueueFull_OrNoServiceKey_ReturnsAiUnavailable()
    {
        (await Client(new StubHandler(_ => throw new TaskCanceledException("timeout"))).StartRunAsync(Job)).ErrorCode
            .Should().Be(ResultErrorCodes.AiUnavailable);
        (await Client(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests))).StartRunAsync(Job)).Message
            .Should().Contain("đầy");
        var noKey = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        (await Client(noKey, key: null).StartRunAsync(Job)).ErrorCode.Should().Be(ResultErrorCodes.AiUnavailable);
        noKey.Calls.Should().Be(0, "không gọi mạng khi chưa có khoá dịch vụ");
    }

    [Fact]
    public async Task AiClient_SendsServiceKeyHeader_AndParsesAck()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted)
            { Content = System.Net.Http.Json.JsonContent.Create(new AiRunAck(true), options: AiJson.Options) });

        var ack = await Client(handler).StartRunAsync(Job);

        ack.Accepted.Should().BeTrue();
        handler.Last!.Headers.GetValues(AiProtocol.ServiceKeyHeader).Single().Should().Be("secret");
        handler.Last.RequestUri!.AbsolutePath.Should().Be("/runs");
    }

    [Fact]
    public void AiRunJob_ToString_NeverRevealsApiKey()
    {
        var job = new AiRunJob(Guid.NewGuid(), "Gemini", "gemini-2.5-flash", "AIza-TOP-SECRET", 2);

        job.ToString().Should().NotContain("AIza-TOP-SECRET");
        $"{job}".Should().NotContain("TOP-SECRET");
    }

    // ── Khoá dịch vụ cho API nội bộ ──────────────────────────────────────────
    private static async Task<IActionResult?> RunFilter(string? expectedKey, string? providedKey, IPAddress? remote)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = remote;
        if (providedKey is not null) http.Request.Headers[AiProtocol.ServiceKeyHeader] = providedKey;
        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var ctx = new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), new object());
        var nextCalled = false;
        await new ServiceKeyAuthFilter(new AiAgentSettings { ServiceKey = expectedKey })
            .OnActionExecutionAsync(ctx, () => { nextCalled = true; return Task.FromResult(new ActionExecutedContext(actionContext, [], new object())); });
        return nextCalled ? null : ctx.Result;
    }

    [Fact]
    public async Task ServiceKeyFilter_AllowsOnlyCorrectKeyFromLoopback()
    {
        (await RunFilter("k", "k", IPAddress.Loopback)).Should().BeNull();
        (await RunFilter("k", "wrong", IPAddress.Loopback)).Should().BeOfType<UnauthorizedObjectResult>();
        (await RunFilter("k", null, IPAddress.Loopback)).Should().BeOfType<UnauthorizedObjectResult>();
        (await RunFilter(null, "", IPAddress.Loopback)).Should().BeOfType<UnauthorizedObjectResult>("chưa cấu hình khoá → từ chối tất cả");
        ((ObjectResult)(await RunFilter("k", "k", IPAddress.Parse("10.0.0.5")))!).StatusCode.Should().Be(403);
    }

    // ── Domain: bất biến của run/suggestion/decision ─────────────────────────
    private static AiWorkflowRun NewRun() => AiWorkflowRun.Create(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Mock", "m", "brief",
        ReadingDirection.RightToLeft, 2, 0, null, "{}", "[]");

    [Fact]
    public void Run_Lifecycle_QueuedToRunning_Terminal_CannotChangeAgain()
    {
        var run = NewRun();
        run.Status.Should().Be(AiRunStatus.Queued);
        run.Touch();
        run.Status.Should().Be(AiRunStatus.Running);
        run.RegisterWriterRound(2);
        var act = () => run.RegisterWriterRound(3);
        act.Should().Throw<InvalidOperationException>();

        run.MarkWaitingForHuman();
        run.Status.Should().Be(AiRunStatus.WaitingForHuman);
        var again = () => run.MarkCompleted();
        again.Should().Throw<InvalidOperationException>();
        run.Touch();
        run.Status.Should().Be(AiRunStatus.WaitingForHuman);
    }

    [Fact]
    public void Suggestion_CanBeDecidedOnlyOnce_AndAcceptNeedsRevision()
    {
        var s = AiSuggestion.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            AiSuggestionKind.Script, "{}", "hash", "s", 0, null, "[]", null, false);
        s.Decide(AiSuggestionStatus.Rejected);
        var again = () => s.Decide(AiSuggestionStatus.Accepted);
        again.Should().Throw<InvalidOperationException>();

        var noRevision = () => AiSuggestionDecision.Create(s.Id, Guid.NewGuid(), AiSuggestionStatus.Accepted, null, 0, null);
        noRevision.Should().Throw<ArgumentException>("BR-10: Accept phải kèm provenance/revision");
        AiSuggestionDecision.Create(s.Id, Guid.NewGuid(), AiSuggestionStatus.Rejected, "lý do", 0, null).Reason.Should().Be("lý do");
    }

    [Fact]
    public void Step_UsageCanBeLinkedOnlyOnce()
    {
        var step = AiAgentStep.Create(Guid.NewGuid(), 1, AiAgentType.Writer, 0, null, AiStepStatus.Succeeded, "Gemini", "m", "w@1", "ref",
            "{}", "{}", "{}", DateTime.UtcNow, DateTime.UtcNow, 1, 1, 0, null, null);
        step.LinkUsageRecord(Guid.NewGuid());
        var again = () => step.LinkUsageRecord(Guid.NewGuid());
        again.Should().Throw<InvalidOperationException>();
    }
}